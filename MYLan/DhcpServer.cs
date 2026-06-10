using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;

namespace DhcpFieldServer
{
    public enum LeaseState
    {
        Offered,
        Active
    }

    public class DhcpLease
    {
        public string Mac { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public DateTime ExpiryUtc { get; set; }
        public LeaseState State { get; set; } = LeaseState.Offered;

        public IPAddress GetIp() => IPAddress.Parse(IpAddress);
        public bool IsExpired => ExpiryUtc <= DateTime.UtcNow;
    }

    /// <summary>Persisted alongside leases so quarantines survive restarts.</summary>
    public class DeclinedIpEntry
    {
        public string IpAddress { get; set; } = "";
        public DateTime ExpiryUtc { get; set; }
    }

    /// <summary>Root object for the lease JSON file.</summary>
    public class LeaseFileData
    {
        public List<DhcpLease> Leases { get; set; } = new();
        public List<DeclinedIpEntry> DeclinedIps { get; set; } = new();
    }

    public class DhcpServer
    {
        private readonly IPAddress _serverIp;
        private readonly IPAddress _poolStart;
        private readonly IPAddress _poolEnd;
        private readonly IPAddress _subnetMask;
        private readonly IPAddress _routerIp;
        private readonly IPAddress _dnsIp;
        private readonly int _leaseSeconds;

        private readonly Dictionary<string, DhcpLease> _leases = new();
        private readonly object _leaseLock = new();

        private readonly Dictionary<string, DateTime> _declinedIps = new();
        private static readonly TimeSpan DeclineQuarantine = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan OfferHoldTime = TimeSpan.FromSeconds(30);

        private readonly string? _leaseFilePath;

        // ── Save debounce (#9) ───────────────────────────────
        private Timer? _saveTimer;
        private volatile bool _savePending;
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(2);

        private UdpClient? _udp;
        private Thread? _listenerThread;
        private volatile bool _running;

        public event Action<string>? Log;

        public IReadOnlyList<DhcpLease> ActiveLeases
        {
            get
            {
                lock (_leaseLock)
                {
                    return _leases.Values
                        .Where(l => l.State == LeaseState.Active && !l.IsExpired)
                        .ToList()
                        .AsReadOnly();
                }
            }
        }

        public DhcpServer(string serverIpAddress, string poolStart, string poolEnd,
                          string subnetMask, string routerIp, string dnsIp,
                          int leaseHours = 8, string? leaseFilePath = null)
        {
            _serverIp    = IPAddress.Parse(serverIpAddress);
            _poolStart   = IPAddress.Parse(poolStart);
            _poolEnd     = IPAddress.Parse(poolEnd);
            _subnetMask  = IPAddress.Parse(subnetMask);
            _routerIp    = IPAddress.Parse(routerIp);
            _dnsIp       = IPAddress.Parse(dnsIp);
            _leaseSeconds = leaseHours * 3600;
            _leaseFilePath = leaseFilePath;

            LoadLeases();
        }

        // ── Lifecycle ────────────────────────────────────────

        public void Start()
        {
            if (_running) return;

            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 67));
                _udp.EnableBroadcast = true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                throw new InvalidOperationException(
                    "UDP port 67 is already in use. Another DHCP server or a previous MYLan instance " +
                    "may still be running. Close it and try again.", ex);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
            {
                throw new InvalidOperationException(
                    "Access denied binding to port 67. Ensure you are running with elevated privileges.", ex);
            }

            _saveTimer = new Timer(_ => FlushSave(), null, Timeout.Infinite, Timeout.Infinite);
            _running = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
            _listenerThread.Start();

            Log?.Invoke("DHCP server started on UDP port 67.");
        }

        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            try { _listenerThread?.Join(500); } catch { }
            try { _saveTimer?.Dispose(); } catch { }

            // Final flush — synchronous, outside debounce
            lock (_leaseLock) { SaveLeasesNow(); }
            Log?.Invoke("DHCP server stopped. Leases saved.");
        }

        // ── Listener ─────────────────────────────────────────

        private void ListenLoop()
        {
            if (_udp == null) return;
            var remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    if (data.Length < 244) continue;
                    if (data[0] != 1) continue;

                    uint xid = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);

                    byte[] macBytes = new byte[6];
                    Array.Copy(data, 28, macBytes, 0, 6);
                    string mac = BitConverter.ToString(macBytes);

                    if (!(data[236] == 99 && data[237] == 130 && data[238] == 83 && data[239] == 99))
                    {
                        Log?.Invoke("Invalid DHCP magic cookie, ignoring.");
                        continue;
                    }

                    var options = ParseOptions(data);

                    switch (options.MessageType)
                    {
                        case 1: HandleDiscover(xid, macBytes, mac); break;
                        case 3: HandleRequest(xid, macBytes, mac, data, options); break;
                        case 4: HandleDecline(mac, options); break;
                        case 7: HandleRelease(mac); break;
                    }
                }
                catch (SocketException) { if (!_running) break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log?.Invoke("Error: " + ex.Message); }
            }
        }

        // ── Message Handlers ─────────────────────────────────

        private void HandleDiscover(uint xid, byte[] macBytes, string mac)
        {
            Log?.Invoke($"DISCOVER from {mac}");

            lock (_leaseLock)
            {
                PurgeExpired();

                // FIX #1: If the MAC already has an ACTIVE lease, don't overwrite it.
                // Just send OFFER with the existing IP — the lease entry stays Active.
                if (_leases.TryGetValue(mac, out var existing) && !existing.IsExpired
                    && existing.State == LeaseState.Active)
                {
                    SendReply(xid, macBytes, existing.GetIp(), 2);
                    Log?.Invoke($"Sent OFFER → {existing.IpAddress} (existing active lease)");
                    return;
                }

                IPAddress? offerIp = AllocateIp(mac);
                if (offerIp == null)
                {
                    Log?.Invoke("[WARN] IP pool exhausted — cannot respond to DISCOVER.");
                    return;
                }

                _leases[mac] = new DhcpLease
                {
                    Mac = mac,
                    IpAddress = offerIp.ToString(),
                    ExpiryUtc = DateTime.UtcNow.Add(OfferHoldTime),
                    State = LeaseState.Offered
                };

                SendReply(xid, macBytes, offerIp, 2);
                Log?.Invoke($"Sent OFFER → {offerIp}");
            }
        }

        private void HandleRequest(uint xid, byte[] macBytes, string mac, byte[] data, DhcpOptions options)
        {
            Log?.Invoke($"REQUEST from {mac}");

            lock (_leaseLock)
            {
                PurgeExpired();

                IPAddress? requestedIp = options.RequestedIp;
                if (requestedIp == null || requestedIp.Equals(IPAddress.Any))
                {
                    byte[] ciBytes = new byte[4];
                    Array.Copy(data, 12, ciBytes, 0, 4);
                    var ci = new IPAddress(ciBytes);
                    if (!ci.Equals(IPAddress.Any))
                        requestedIp = ci;
                }

                if (requestedIp != null && !requestedIp.Equals(IPAddress.Any))
                {
                    if (!IsInPool(requestedIp))
                    {
                        Log?.Invoke($"[NAK] {mac} requested {requestedIp} — outside pool.");
                        SendNak(xid, macBytes); return;
                    }

                    if (IsDeclined(requestedIp))
                    {
                        Log?.Invoke($"[NAK] {mac} requested {requestedIp} — quarantined (DECLINE).");
                        SendNak(xid, macBytes); return;
                    }

                    // FIX #2: Check ALL non-expired leases, not just Active
                    var conflict = _leases.Values.FirstOrDefault(l =>
                        l.Mac != mac &&
                        l.IpAddress == requestedIp.ToString() &&
                        !l.IsExpired);

                    if (conflict != null)
                    {
                        Log?.Invoke($"[NAK] {mac} requested {requestedIp} — held by {conflict.Mac} ({conflict.State}).");
                        SendNak(xid, macBytes); return;
                    }

                    CommitLease(mac, requestedIp);
                    SendReply(xid, macBytes, requestedIp, 5);
                    Log?.Invoke($"Sent ACK → {requestedIp}");
                    ScheduleSave(); return;
                }

                if (_leases.TryGetValue(mac, out var existing) && !existing.IsExpired)
                {
                    var ip = existing.GetIp();

                    // FIX #2 (cont): Also check for conflict when honouring an OFFER
                    var conflict2 = _leases.Values.FirstOrDefault(l =>
                        l.Mac != mac &&
                        l.IpAddress == ip.ToString() &&
                        !l.IsExpired);

                    if (conflict2 != null)
                    {
                        Log?.Invoke($"[NAK] {mac} offered {ip} but now held by {conflict2.Mac}.");
                        _leases.Remove(mac);
                        SendNak(xid, macBytes); return;
                    }

                    CommitLease(mac, ip);
                    SendReply(xid, macBytes, ip, 5);
                    Log?.Invoke($"Sent ACK → {ip}");
                    ScheduleSave(); return;
                }

                IPAddress? freshIp = AllocateIp(mac);
                if (freshIp == null)
                {
                    Log?.Invoke("[WARN] IP pool exhausted — cannot ACK.");
                    SendNak(xid, macBytes); return;
                }

                CommitLease(mac, freshIp);
                SendReply(xid, macBytes, freshIp, 5);
                Log?.Invoke($"Sent ACK → {freshIp}");
                ScheduleSave();
            }
        }

        private void HandleDecline(string mac, DhcpOptions options)
        {
            Log?.Invoke($"DECLINE from {mac}");

            lock (_leaseLock)
            {
                IPAddress? declinedIp = options.RequestedIp;
                if (declinedIp == null && _leases.TryGetValue(mac, out var lease))
                    declinedIp = lease.GetIp();

                if (declinedIp != null)
                {
                    _declinedIps[declinedIp.ToString()] = DateTime.UtcNow.Add(DeclineQuarantine);
                    Log?.Invoke($"Quarantined {declinedIp} for {DeclineQuarantine.TotalMinutes} min (conflict).");
                }

                _leases.Remove(mac);
                ScheduleSave();
            }
        }

        private void HandleRelease(string mac)
        {
            Log?.Invoke($"RELEASE from {mac}");

            lock (_leaseLock)
            {
                if (_leases.Remove(mac))
                {
                    Log?.Invoke($"Lease released for {mac}.");
                    ScheduleSave();
                }
            }
        }

        // ── IP Allocation ────────────────────────────────────

        private IPAddress? AllocateIp(string mac)
        {
            if (_leases.TryGetValue(mac, out var existing) && !existing.IsExpired)
                return existing.GetIp();

            // FIX #4: Proper multi-octet increment using uint arithmetic
            uint start = IpToUint(_poolStart);
            uint end   = IpToUint(_poolEnd);

            for (uint candidate = start; candidate <= end; candidate++)
            {
                string candidateStr = UintToIp(candidate).ToString();

                bool inUse = _leases.Values.Any(l =>
                    l.IpAddress == candidateStr && !l.IsExpired);

                if (!inUse && !IsDeclined(UintToIp(candidate)))
                    return UintToIp(candidate);
            }

            return null;
        }

        private void CommitLease(string mac, IPAddress ip)
        {
            _leases[mac] = new DhcpLease
            {
                Mac = mac,
                IpAddress = ip.ToString(),
                ExpiryUtc = DateTime.UtcNow.AddSeconds(_leaseSeconds),
                State = LeaseState.Active
            };
            Log?.Invoke($"Committed lease {ip} → {mac} (expires {_leases[mac].ExpiryUtc.ToLocalTime():HH:mm:ss})");
        }

        private void PurgeExpired()
        {
            var stale = _leases
                .Where(kv => kv.Value.State == LeaseState.Offered && kv.Value.IsExpired)
                .Select(kv => kv.Key).ToList();

            foreach (var mac in stale)
            {
                Log?.Invoke($"Offer expired for {mac} — IP returned to pool.");
                _leases.Remove(mac);
            }

            var expiredDeclines = _declinedIps
                .Where(kv => kv.Value <= DateTime.UtcNow)
                .Select(kv => kv.Key).ToList();
            foreach (var ip in expiredDeclines)
                _declinedIps.Remove(ip);
        }

        // ── Validation ───────────────────────────────────────

        private bool IsInPool(IPAddress ip)
            => CompareIp(ip, _poolStart) >= 0 && CompareIp(ip, _poolEnd) <= 0;

        private bool IsDeclined(IPAddress ip)
            => _declinedIps.TryGetValue(ip.ToString(), out var until) && until > DateTime.UtcNow;

        private static int CompareIp(IPAddress a, IPAddress b)
        {
            byte[] ab = a.GetAddressBytes();
            byte[] bb = b.GetAddressBytes();
            for (int i = 0; i < ab.Length; i++)
            {
                int d = ab[i].CompareTo(bb[i]);
                if (d != 0) return d;
            }
            return 0;
        }

        // FIX #4: Safe IP arithmetic using uint
        private static uint IpToUint(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        }

        private static IPAddress UintToIp(uint val)
        {
            return new IPAddress(new[]
            {
                (byte)(val >> 24), (byte)(val >> 16),
                (byte)(val >> 8),  (byte)val
            });
        }

        // ── Option Parsing ───────────────────────────────────

        private record DhcpOptions(byte MessageType, IPAddress? RequestedIp, IPAddress? ServerIdentifier);

        private static DhcpOptions ParseOptions(byte[] data)
        {
            byte msgType = 0;
            IPAddress? requestedIp = null;
            IPAddress? serverId = null;

            int idx = 240;
            while (idx < data.Length)
            {
                byte option = data[idx++];
                if (option == 255) break;
                if (option == 0) continue;
                if (idx >= data.Length) break;
                byte len = data[idx++];
                if (idx + len > data.Length) break;

                switch (option)
                {
                    case 53 when len == 1:
                        msgType = data[idx]; break;
                    case 50 when len == 4:
                        requestedIp = new IPAddress(new ReadOnlySpan<byte>(data, idx, 4)); break;
                    case 54 when len == 4:
                        serverId = new IPAddress(new ReadOnlySpan<byte>(data, idx, 4)); break;
                }
                idx += len;
            }

            return new DhcpOptions(msgType, requestedIp, serverId);
        }

        // ── Packet Builders ──────────────────────────────────

        private void SendReply(uint xid, byte[] clientMac, IPAddress yiaddr, byte dhcpMessageType)
        {
            byte[] packet = BuildReplyPacket(xid, clientMac, yiaddr, dhcpMessageType);
            try { _udp?.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 68)); }
            catch (ObjectDisposedException) { }
        }

        private void SendNak(uint xid, byte[] clientMac)
        {
            byte[] packet = new byte[300];
            packet[0] = 2; packet[1] = 1; packet[2] = 6;
            packet[4] = (byte)((xid >> 24) & 0xFF);
            packet[5] = (byte)((xid >> 16) & 0xFF);
            packet[6] = (byte)((xid >> 8) & 0xFF);
            packet[7] = (byte)(xid & 0xFF);
            packet[10] = 0x80; packet[11] = 0x00;
            Array.Copy(clientMac, 0, packet, 28, 6);
            packet[236] = 99; packet[237] = 130; packet[238] = 83; packet[239] = 99;

            int idx = 240;
            packet[idx++] = 53; packet[idx++] = 1; packet[idx++] = 6;
            packet[idx++] = 54; packet[idx++] = 4;
            Array.Copy(_serverIp.GetAddressBytes(), 0, packet, idx, 4); idx += 4;
            packet[idx++] = 255;

            byte[] final = new byte[idx];
            Array.Copy(packet, final, idx);
            try { _udp?.Send(final, final.Length, new IPEndPoint(IPAddress.Broadcast, 68)); }
            catch (ObjectDisposedException) { }
            Log?.Invoke("Sent NAK.");
        }

        private byte[] BuildReplyPacket(uint xid, byte[] clientMac, IPAddress yiaddr, byte dhcpMessageType)
        {
            byte[] packet = new byte[300];
            packet[0] = 2; packet[1] = 1; packet[2] = 6; packet[3] = 0;
            packet[4] = (byte)((xid >> 24) & 0xFF);
            packet[5] = (byte)((xid >> 16) & 0xFF);
            packet[6] = (byte)((xid >> 8) & 0xFF);
            packet[7] = (byte)(xid & 0xFF);
            packet[10] = 0x80; packet[11] = 0x00;
            Array.Copy(yiaddr.GetAddressBytes(), 0, packet, 16, 4);
            Array.Copy(_serverIp.GetAddressBytes(), 0, packet, 20, 4);
            Array.Copy(clientMac, 0, packet, 28, 6);
            packet[236] = 99; packet[237] = 130; packet[238] = 83; packet[239] = 99;

            int idx = 240;
            packet[idx++] = 53; packet[idx++] = 1; packet[idx++] = dhcpMessageType;
            packet[idx++] = 54; packet[idx++] = 4;
            Array.Copy(_serverIp.GetAddressBytes(), 0, packet, idx, 4); idx += 4;
            packet[idx++] = 1; packet[idx++] = 4;
            Array.Copy(_subnetMask.GetAddressBytes(), 0, packet, idx, 4); idx += 4;
            packet[idx++] = 3; packet[idx++] = 4;
            Array.Copy(_routerIp.GetAddressBytes(), 0, packet, idx, 4); idx += 4;
            packet[idx++] = 6; packet[idx++] = 4;
            Array.Copy(_dnsIp.GetAddressBytes(), 0, packet, idx, 4); idx += 4;
            packet[idx++] = 51; packet[idx++] = 4;
            packet[idx++] = (byte)((_leaseSeconds >> 24) & 0xFF);
            packet[idx++] = (byte)((_leaseSeconds >> 16) & 0xFF);
            packet[idx++] = (byte)((_leaseSeconds >> 8) & 0xFF);
            packet[idx++] = (byte)(_leaseSeconds & 0xFF);
            packet[idx++] = 255;

            byte[] final = new byte[idx];
            Array.Copy(packet, final, idx);
            return final;
        }

        // ── Lease Persistence ────────────────────────────────

        // FIX #9: Debounced save — schedule via timer, flush on stop
        private void ScheduleSave()
        {
            _savePending = true;
            _saveTimer?.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
        }

        private void FlushSave()
        {
            if (!_savePending) return;
            lock (_leaseLock) { SaveLeasesNow(); }
        }

        // FIX #6: Atomic write via temp file + rename
        // FIX #7: Persist declined IPs alongside leases
        // FIX #8: All timestamps are UTC
        private void SaveLeasesNow()
        {
            if (_leaseFilePath == null) return;
            _savePending = false;

            try
            {
                var data = new LeaseFileData
                {
                    Leases = _leases.Values
                        .Where(l => l.State == LeaseState.Active && !l.IsExpired)
                        .ToList(),
                    DeclinedIps = _declinedIps
                        .Where(kv => kv.Value > DateTime.UtcNow)
                        .Select(kv => new DeclinedIpEntry { IpAddress = kv.Key, ExpiryUtc = kv.Value })
                        .ToList()
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                string tmpPath = _leaseFilePath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _leaseFilePath, overwrite: true);
            }
            catch (Exception ex) { Log?.Invoke($"[WARN] Failed to save leases: {ex.Message}"); }
        }

        private void LoadLeases()
        {
            if (_leaseFilePath == null || !File.Exists(_leaseFilePath)) return;
            try
            {
                string json = File.ReadAllText(_leaseFilePath);
                if (string.IsNullOrWhiteSpace(json)) return;

                LeaseFileData? data = null;

                // Try new format first (LeaseFileData wrapper)
                try { data = JsonSerializer.Deserialize<LeaseFileData>(json); }
                catch (JsonException) { }

                // Fall back to old format (bare List<DhcpLease>)
                if (data == null || (data.Leases.Count == 0 && json.TrimStart().StartsWith("[")))
                {
                    try
                    {
                        var oldLeases = JsonSerializer.Deserialize<List<DhcpLease>>(json);
                        if (oldLeases != null)
                            data = new LeaseFileData { Leases = oldLeases };
                    }
                    catch (JsonException) { }
                }

                if (data == null) return;

                lock (_leaseLock)
                {
                    int restored = 0;
                    foreach (var lease in data.Leases)
                    {
                        if (!lease.IsExpired && !string.IsNullOrEmpty(lease.Mac))
                        {
                            lease.State = LeaseState.Active;
                            _leases[lease.Mac] = lease;
                            restored++;
                        }
                    }

                    foreach (var declined in data.DeclinedIps)
                    {
                        if (declined.ExpiryUtc > DateTime.UtcNow)
                            _declinedIps[declined.IpAddress] = declined.ExpiryUtc;
                    }

                    if (restored > 0)
                        Log?.Invoke($"Restored {restored} active lease(s) from disk.");
                    if (_declinedIps.Count > 0)
                        Log?.Invoke($"Restored {_declinedIps.Count} quarantined IP(s) from disk.");
                }
            }
            catch (Exception ex) { Log?.Invoke($"[WARN] Failed to load leases: {ex.Message}"); }
        }
    }
}
