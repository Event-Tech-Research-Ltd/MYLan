using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DhcpFieldServer.Services;

namespace DhcpFieldServer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private DhcpServer? _server;
    private AdapterIpConfiguration? _originalAdapterConfig;

    /// <summary>
    /// Lease file stored in per-user application data so installers can live in read-only locations.
    /// </summary>
    private static readonly string LeaseFilePath = GetWritableLeaseFilePath();

    private static string GetWritableLeaseFilePath()
    {
        try
        {
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(basePath))
                basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(basePath))
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mylan");

            string directory = Path.Combine(basePath, "MYLan");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "mylan-leases.json");
        }
        catch
        {
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mylan", "mylan-leases.json");
            Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
            return fallback;
        }
    }

    private static readonly int MaxLogLines = 2000;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private NetworkAdapterInfo? _selectedAdapter;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _statusColour = "#888888";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private string _serverIp = "192.168.1.1";

    partial void OnServerIpChanged(string value)
    {
        // Gateway defaults to match server IP unless the user has overridden it
        if (Gateway == _lastAutoGateway || string.IsNullOrEmpty(Gateway))
        {
            Gateway = value;
            _lastAutoGateway = value;
        }
    }

    private string _lastAutoGateway = "192.168.1.1";

    [ObservableProperty]
    private string _subnetMask = "255.255.255.0";

    [ObservableProperty]
    private string _poolStart = "192.168.1.50";

    [ObservableProperty]
    private string _poolEnd = "192.168.1.150";

    [ObservableProperty]
    private string _gateway = "192.168.1.1";

    [ObservableProperty]
    private string _dnsServer = "8.8.8.8";

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private string _platformHint = "";

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = new();

    public ObservableCollection<LeaseDisplayItem> Leases { get; } = new();

    public MainWindowViewModel()
    {
        IsElevated = NetworkHelper.HasElevatedPrivileges();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            PlatformHint = IsElevated ? "" : "\u26a0 Not running as Administrator \u2014 right-click \u2192 Run as Administrator";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            PlatformHint = IsElevated ? "" : "\u26a0 Not running as root \u2014 launch with: sudo ./MYLan";
        else
            PlatformHint = IsElevated ? "" : "\u26a0 Elevated privileges required \u2014 launch with: sudo ./MYLan";

        RefreshAdapters();
    }

    [RelayCommand]
    private void RefreshAdapters()
    {
        Adapters.Clear();
        foreach (var adapter in NetworkHelper.GetAvailableAdapters())
            Adapters.Add(adapter);

        if (Adapters.Count > 0 && SelectedAdapter == null)
            SelectedAdapter = Adapters[0];
    }

    private bool CanStart() => !IsRunning && SelectedAdapter != null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (SelectedAdapter == null) return;

        // ── Validate all IP fields before doing anything ──
        var fields = new (string Label, string Value)[]
        {
            ("Server IP", ServerIp), ("Subnet Mask", SubnetMask),
            ("Pool Start", PoolStart), ("Pool End", PoolEnd),
            ("Gateway", Gateway), ("DNS", DnsServer)
        };

        foreach (var (label, value) in fields)
        {
            if (!TryParseIpv4(value, out _))
            {
                AppendLog($"[ERROR] Invalid {label}: \"{value}\" - enter a valid IPv4 address.");
                return;
            }
        }

        try
        {
            _ = NetworkHelper.NetmaskToCidr(SubnetMask);
        }
        catch
        {
            AppendLog("[ERROR] Subnet Mask must be a contiguous IPv4 mask, for example 255.255.255.0.");
            return;
        }

        var serverIp = IPAddress.Parse(ServerIp);
        var subnetMask = IPAddress.Parse(SubnetMask);
        var poolStartIp = IPAddress.Parse(PoolStart);
        var poolEndIp = IPAddress.Parse(PoolEnd);
        if (CompareIpBytes(poolStartIp, poolEndIp) > 0)
        {
            AppendLog("[ERROR] Pool Start must be less than or equal to Pool End.");
            return;
        }

        if (!IsInSameSubnet(poolStartIp, serverIp, subnetMask) ||
            !IsInSameSubnet(poolEndIp, serverIp, subnetMask))
        {
            AppendLog("[ERROR] Pool range must be in the same subnet as the Server IP.");
            return;
        }

        AppendLog($"Using adapter: {SelectedAdapter.DisplayName}");
        _originalAdapterConfig = NetworkHelper.CaptureAdapterConfiguration(SelectedAdapter.Name);
        if (_originalAdapterConfig == null)
            AppendLog("[WARN] Could not capture previous adapter IP configuration; automatic restore may be unavailable.");

        // Configure static IP on the selected adapter
        AppendLog($"Setting static IP {ServerIp}/{SubnetMask} on adapter...");
        bool ipOk = NetworkHelper.SetStaticIp(SelectedAdapter.Name, ServerIp, SubnetMask, Gateway);

        if (!ipOk)
        {
            RestoreOriginalAdapterConfiguration();
            string hint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Are you running as Administrator?"
                : "Are you running as root (sudo)?";
            AppendLog($"[ERROR] Failed to set static IP. {hint}");
            return;
        }

        AppendLog("Static IP configured successfully.");

        try
        {
            _server = new DhcpServer(
                serverIpAddress: ServerIp,
                poolStart: PoolStart,
                poolEnd: PoolEnd,
                subnetMask: SubnetMask,
                routerIp: Gateway,
                dnsIp: DnsServer,
                leaseFilePath: LeaseFilePath);

            _server.Log += msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    AppendLog(msg);
                    RefreshLeases();
                });
            };

            _server.Start();

            IsRunning = true;
            StatusText = "DHCP Running";
            StatusColour = "#4CAF50";

            // Show restored lease count
            int restored = _server.ActiveLeases.Count;
            if (restored > 0)
                AppendLog($"{restored} lease(s) restored from previous session.");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            try { _server?.Stop(); } catch { }
            _server = null;
            RestoreOriginalAdapterConfiguration();
        }
    }

    [RelayCommand]
    private void Stop()
    {
        try
        {
            _server?.Stop();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            RestoreOriginalAdapterConfiguration();
        }

        IsRunning = false;
        StatusText = "Stopped";
        StatusColour = "#F44336";
        _server = null;
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogText = "";
    }

    private void RefreshLeases()
    {
        if (_server == null) return;

        var active = _server.ActiveLeases;
        Leases.Clear();
        foreach (var lease in active)
        {
            Leases.Add(new LeaseDisplayItem
            {
                Mac = lease.Mac,
                IpAddress = lease.IpAddress,
                Expiry = lease.ExpiryUtc.ToLocalTime().ToString("HH:mm:ss")
            });
        }
    }

    private void AppendLog(string message)
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

        // Cap log to prevent unbounded memory growth during long sessions
        var lines = LogText.Split(Environment.NewLine);
        if (lines.Length > MaxLogLines)
        {
            LogText = string.Join(Environment.NewLine,
                lines.Skip(lines.Length - MaxLogLines));
        }
    }

    private void RestoreOriginalAdapterConfiguration()
    {
        if (_originalAdapterConfig == null) return;

        AppendLog("Restoring previous adapter IP configuration...");
        if (NetworkHelper.RestoreAdapterConfiguration(_originalAdapterConfig))
            AppendLog("Previous adapter IP configuration restored.");
        else
            AppendLog("[WARN] Could not restore previous adapter IP configuration automatically.");

        _originalAdapterConfig = null;
    }

    private static bool TryParseIpv4(string value, out IPAddress address)
    {
        if (IPAddress.TryParse(value, out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsed;
            return true;
        }

        address = IPAddress.None;
        return false;
    }

    private static bool IsInSameSubnet(IPAddress address, IPAddress serverIp, IPAddress subnetMask)
    {
        uint mask = IpToUint(subnetMask);
        return (IpToUint(address) & mask) == (IpToUint(serverIp) & mask);
    }

    private static uint IpToUint(IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        if (b.Length != 4)
            throw new ArgumentException("Only IPv4 addresses are supported.", nameof(ip));

        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static int CompareIpBytes(IPAddress a, IPAddress b)
    {
        byte[] ab = a.GetAddressBytes();
        byte[] bb = b.GetAddressBytes();
        if (ab.Length != 4 || bb.Length != 4)
            throw new ArgumentException("Only IPv4 addresses are supported.");

        for (int i = 0; i < ab.Length; i++)
        {
            int d = ab[i].CompareTo(bb[i]);
            if (d != 0) return d;
        }
        return 0;
    }
}

public class LeaseDisplayItem
{
    public string Mac { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Expiry { get; set; } = "";
}
