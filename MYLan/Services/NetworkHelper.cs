using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DhcpFieldServer.Services;

/// <summary>
/// Snapshot of adapter IP settings before MYLan applies a temporary static address.
/// </summary>
public sealed class AdapterIpConfiguration
{
    public string AdapterName { get; init; } = "";
    public bool IsDhcpEnabled { get; init; }
    public string? IpAddress { get; init; }
    public string? SubnetMask { get; init; }
    public string? Gateway { get; init; }
    public List<string> DnsServers { get; init; } = new();
    public string? MacHardwarePort { get; init; }
    public int? LinuxPrefixLength { get; init; }
}

/// <summary>
/// Cross-platform network adapter configuration.
/// Windows: netsh | macOS: networksetup / ifconfig | Linux: ip
/// </summary>
public static class NetworkHelper
{
    public static bool SetStaticIp(string adapterName, string ip, string netmask, string? gateway = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SetStaticIpWindows(adapterName, ip, netmask);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SetStaticIpMac(adapterName, ip, netmask, gateway);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return SetStaticIpLinux(adapterName, ip, netmask);

        return false;
    }

    public static AdapterIpConfiguration? CaptureAdapterConfiguration(string adapterName)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CaptureWindowsConfiguration(adapterName);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return CaptureMacConfiguration(adapterName);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return CaptureLinuxConfiguration(adapterName);
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static bool RestoreAdapterConfiguration(AdapterIpConfiguration? config)
    {
        if (config == null) return true;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return RestoreWindowsConfiguration(config);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return RestoreMacConfiguration(config);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return RestoreLinuxConfiguration(config);
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Polls until the OS has actually assigned the address (netsh/networksetup return
    /// before the address is usable). Binding port 67 before that fails on Windows.
    /// </summary>
    public static bool WaitForIp(string adapterName, string ip, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var nic = FindInterface(adapterName);
            if (nic != null && nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == ip))
                return true;
            Thread.Sleep(250);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    public static List<NetworkAdapterInfo> GetAvailableAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        string macPorts = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? RunCommand("networksetup", "-listallhardwareports") : "";

        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n =>
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                n.OperationalStatus == OperationalStatus.Up &&
                !IsWirelessOrVirtual(n, macPorts));

        foreach (var nic in nics)
        {
            string displayName = nic.Name;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                !string.IsNullOrEmpty(nic.Description) &&
                nic.Description != nic.Name)
            {
                displayName = $"{nic.Name} - {nic.Description}";
            }

            adapters.Add(new NetworkAdapterInfo
            {
                Name = nic.Name,
                DisplayName = displayName,
                Id = nic.Id,
                InterfaceType = nic.NetworkInterfaceType.ToString(),
                MacAddress = nic.GetPhysicalAddress().ToString()
            });
        }

        return adapters;
    }

    /// <summary>
    /// Wi-Fi is excluded: making the Wi-Fi client adapter static kills the machine's
    /// internet and would run DHCP on the venue's Wi-Fi. .NET reports Wi-Fi as
    /// "Ethernet" on macOS and Linux, so each OS is checked its own way.
    /// </summary>
    private static bool IsWirelessOrVirtual(NetworkInterface nic, string macHardwarePorts)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            return true;

        if (OperatingSystem.IsLinux())
            return Directory.Exists($"/sys/class/net/{nic.Name}/wireless");

        if (OperatingSystem.IsMacOS())
        {
            // AirDrop/low-latency WLAN, VPN tunnels, hotspot and bridge interfaces.
            string[] virtualPrefixes = { "awdl", "llw", "utun", "anpi", "ap", "bridge", "gif", "stf", "ipsec" };
            if (virtualPrefixes.Any(p => nic.Name.StartsWith(p, StringComparison.Ordinal)))
                return true;
            string? port = HardwarePortFor(macHardwarePorts, nic.Name);
            return port != null && (port.Equals("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                                    port.Equals("AirPort", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    /// <summary>
    /// Name of another up adapter that already has an address in serverIp's subnet,
    /// e.g. venue Wi-Fi on 192.168.1.0/24, or null. Two adapters on one subnet breaks both.
    /// </summary>
    public static string? FindSubnetConflict(string adapterName, string serverIp, string netmask)
    {
        uint mask = ToUint(IPAddress.Parse(netmask));
        uint net = ToUint(IPAddress.Parse(serverIp)) & mask;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.Name == adapterName || nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var a in nic.GetIPProperties().UnicastAddresses)
                if (a.Address.AddressFamily == AddressFamily.InterNetwork && (ToUint(a.Address) & mask) == net)
                    return $"{nic.Name} ({a.Address})";
        }
        return null;
    }

    private static uint ToUint(IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    public static bool HasElevatedPrivileges()
    {
        if (OperatingSystem.IsWindows())
            return IsWindowsAdmin();

        try
        {
            var result = RunCommand("id", "-u");
            return result.Trim() == "0";
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsWindowsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static NetworkInterface? FindInterface(string adapterName)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n =>
                n.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase) ||
                n.Id.Equals(adapterName, StringComparison.OrdinalIgnoreCase) ||
                n.Description.Equals(adapterName, StringComparison.OrdinalIgnoreCase));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static AdapterIpConfiguration? CaptureWindowsConfiguration(string adapterName)
    {
        var nic = FindInterface(adapterName);
        if (nic == null) return null;

        var props = nic.GetIPProperties();
        var ipv4Props = props.GetIPv4Properties();
        var unicast = props.UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

        return new AdapterIpConfiguration
        {
            AdapterName = adapterName,
            IsDhcpEnabled = ipv4Props?.IsDhcpEnabled ?? false,
            IpAddress = unicast?.Address.ToString(),
            SubnetMask = unicast?.IPv4Mask?.ToString(),
            Gateway = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.Address.ToString(),
            DnsServers = props.DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList()
        };
    }

    private static AdapterIpConfiguration CaptureMacConfiguration(string adapterName)
    {
        string? hardwarePort = ResolveMacHardwarePort(adapterName);
        string port = hardwarePort ?? adapterName;
        string output = RunCommand("networksetup", "-getinfo", port);

        return new AdapterIpConfiguration
        {
            AdapterName = adapterName,
            MacHardwarePort = port,
            IsDhcpEnabled = output.Contains("DHCP Configuration", StringComparison.OrdinalIgnoreCase),
            IpAddress = MatchValue(output, "IP address"),
            SubnetMask = MatchValue(output, "Subnet mask"),
            Gateway = MatchValue(output, "Router")
        };
    }

    private static AdapterIpConfiguration CaptureLinuxConfiguration(string adapterName)
    {
        string output = RunCommand("ip", "-4", "-o", "addr", "show", "dev", adapterName);
        var match = Regex.Match(output, @"inet\s+(?<ip>\d+\.\d+\.\d+\.\d+)/(?<prefix>\d+)");
        int? prefix = match.Success ? int.Parse(match.Groups["prefix"].Value) : null;

        return new AdapterIpConfiguration
        {
            AdapterName = adapterName,
            IsDhcpEnabled = output.Contains(" dynamic ", StringComparison.OrdinalIgnoreCase),
            IpAddress = match.Success ? match.Groups["ip"].Value : null,
            SubnetMask = prefix.HasValue ? CidrToNetmask(prefix.Value) : null,
            LinuxPrefixLength = prefix
        };
    }

    private static bool RestoreWindowsConfiguration(AdapterIpConfiguration config)
    {
        bool ok;
        if (config.IsDhcpEnabled)
        {
            ok = RunCommandSuccess("netsh", "interface", "ip", "set", "address", $"name={config.AdapterName}", "dhcp");
            RunCommandSuccess("netsh", "interface", "ip", "set", "dns", $"name={config.AdapterName}", "dhcp");
            return ok;
        }

        if (string.IsNullOrWhiteSpace(config.IpAddress) || string.IsNullOrWhiteSpace(config.SubnetMask))
            return false;

        ok = RunCommandSuccess("netsh", "interface", "ip", "set", "address",
            $"name={config.AdapterName}", "static", config.IpAddress, config.SubnetMask,
            string.IsNullOrWhiteSpace(config.Gateway) ? "none" : config.Gateway);

        RestoreWindowsDns(config);
        return ok;
    }

    private static void RestoreWindowsDns(AdapterIpConfiguration config)
    {
        if (config.DnsServers.Count == 0)
            return;

        RunCommandSuccess("netsh", "interface", "ip", "set", "dns",
            $"name={config.AdapterName}", "static", config.DnsServers[0], "primary");

        for (int i = 1; i < config.DnsServers.Count; i++)
        {
            RunCommandSuccess("netsh", "interface", "ip", "add", "dns",
                $"name={config.AdapterName}", config.DnsServers[i], $"index={i + 1}");
        }
    }

    private static bool RestoreMacConfiguration(AdapterIpConfiguration config)
    {
        string port = config.MacHardwarePort ?? ResolveMacHardwarePort(config.AdapterName) ?? config.AdapterName;
        if (config.IsDhcpEnabled)
        {
            // Undo the temporary `ipconfig set MANUAL` first; -setdhcp alone is a no-op when the
            // saved service config is already DHCP, which would leave the address in place.
            bool ipcfg = RunCommandSuccess("ipconfig", "set", config.AdapterName, "DHCP");
            bool svc = RunCommandSuccess("networksetup", "-setdhcp", port);
            return ipcfg || svc;
        }

        if (string.IsNullOrWhiteSpace(config.IpAddress) || string.IsNullOrWhiteSpace(config.SubnetMask))
            return false;

        // Put the original address back directly (saved service config may be unchanged, so
        // networksetup alone would not re-apply it), then re-save the service settings.
        bool direct = RunCommandSuccess("ipconfig", "set", config.AdapterName, "MANUAL", config.IpAddress, config.SubnetMask);
        string router = string.IsNullOrWhiteSpace(config.Gateway) ? config.IpAddress : config.Gateway;
        bool saved = RunCommandSuccess("networksetup", "-setmanual", port, config.IpAddress, config.SubnetMask, router);
        return direct || saved;
    }

    private static bool RestoreLinuxConfiguration(AdapterIpConfiguration config)
    {
        RunCommandSuccess("ip", "-4", "addr", "flush", "dev", config.AdapterName);

        if (config.IsDhcpEnabled)
        {
            if (RunCommandSuccess("dhclient", config.AdapterName)) return true;
            if (RunCommandSuccess("dhcpcd", config.AdapterName)) return true;
            return RunCommandSuccess("nmcli", "device", "reapply", config.AdapterName);
        }

        if (string.IsNullOrWhiteSpace(config.IpAddress) || config.LinuxPrefixLength == null)
            return true;

        return RunCommandSuccess("ip", "addr", "add",
            $"{config.IpAddress}/{config.LinuxPrefixLength}", "dev", config.AdapterName);
    }

    private static bool SetStaticIpWindows(string adapterName, string ip, string netmask)
    {
        // netsh fails ("object already exists") when the address is already set - treat as success.
        var existing = FindInterface(adapterName);
        if (existing != null && existing.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == ip))
            return true;
        try
        {
            return RunCommandSuccess("netsh", "interface", "ip", "set", "address",
                $"name={adapterName}", "static", ip, netmask);
        }
        catch { return false; }
    }

    private static bool SetStaticIpMac(string adapterName, string ip, string netmask, string? gateway)
    {
        try
        {
            // `networksetup -setmanual` needs a router, and the old code used this Mac's own IP.
            // If Ethernet sits above Wi-Fi in the service order, that makes the show network
            // the default route and cuts the Mac's internet. `ipconfig set ... MANUAL` assigns
            // the address with no router. Restore re-applies the service's saved config.
            string bsdName = adapterName.Contains(" - ", StringComparison.Ordinal)
                ? adapterName.Split(" - ", StringSplitOptions.None)[0].Trim()
                : adapterName;
            if (RunCommandSuccess("ipconfig", "set", bsdName, "MANUAL", ip, netmask))
                return true;
            return RunCommandSuccess("ifconfig", bsdName, ip, "netmask", netmask);
        }
        catch { return false; }
    }

    private static string? ResolveMacHardwarePort(string nameOrBsd)
        => HardwarePortFor(RunCommand("networksetup", "-listallhardwareports"), nameOrBsd);

    private static string? HardwarePortFor(string output, string nameOrBsd)
    {
        try
        {
            var lines = output.Split('\n', StringSplitOptions.TrimEntries);

            string? currentPort = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("Hardware Port:", StringComparison.OrdinalIgnoreCase))
                    currentPort = line["Hardware Port:".Length..].Trim();
                else if (line.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                {
                    string device = line["Device:".Length..].Trim();
                    if (device.Equals(nameOrBsd, StringComparison.OrdinalIgnoreCase) ||
                        (currentPort != null && currentPort.Equals(nameOrBsd, StringComparison.OrdinalIgnoreCase)))
                        return currentPort;
                }
            }
        }
        catch { }
        return null;
    }

    private static bool SetStaticIpLinux(string adapterName, string ip, string netmask)
    {
        try
        {
            string cidr = NetmaskToCidr(netmask).ToString();
            bool flushed = RunCommandSuccess("ip", "-4", "addr", "flush", "dev", adapterName);
            bool added = RunCommandSuccess("ip", "addr", "add", $"{ip}/{cidr}", "dev", adapterName);
            bool up = RunCommandSuccess("ip", "link", "set", "dev", adapterName, "up");
            return flushed && added && up;
        }
        catch { return false; }
    }

    public static int NetmaskToCidr(string netmask)
    {
        var bytes = IPAddress.Parse(netmask).GetAddressBytes();
        if (bytes.Length != 4)
            throw new ArgumentException("Subnet mask must be IPv4.", nameof(netmask));

        int cidr = 0;
        bool zeroSeen = false;

        foreach (byte b in bytes)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                bool isOne = (b & (1 << bit)) != 0;
                if (isOne)
                {
                    if (zeroSeen)
                        throw new ArgumentException("Subnet mask bits must be contiguous.", nameof(netmask));
                    cidr++;
                }
                else
                {
                    zeroSeen = true;
                }
            }
        }

        return cidr;
    }

    private static string CidrToNetmask(int cidr)
    {
        if (cidr < 0 || cidr > 32)
            throw new ArgumentOutOfRangeException(nameof(cidr));

        uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        return new IPAddress(new[]
        {
            (byte)(mask >> 24),
            (byte)(mask >> 16),
            (byte)(mask >> 8),
            (byte)mask
        }).ToString();
    }

    private static string? MatchValue(string output, string label)
    {
        var match = Regex.Match(output, $"^{Regex.Escape(label)}:\\s*(?<value>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (!match.Success) return null;

        string value = match.Groups["value"].Value.Trim();
        return value.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static bool RunCommandSuccess(string fileName, params string[] arguments)
    {
        try
        {
            var psi = CreateStartInfo(fileName, arguments);
            using var p = Process.Start(psi);
            if (p == null) return false;

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(10_000))
            {
                try { p.Kill(); } catch { }
                return false;
            }

            stdoutTask.GetAwaiter().GetResult();
            stderrTask.GetAwaiter().GetResult();

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string RunCommand(string fileName, params string[] arguments)
    {
        try
        {
            var psi = CreateStartInfo(fileName, arguments);
            using var p = Process.Start(psi);
            if (p == null) return "";

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(5_000))
            {
                try { p.Kill(); } catch { }
                return "";
            }

            stderrTask.GetAwaiter().GetResult();
            return stdoutTask.GetAwaiter().GetResult();
        }
        catch
        {
            return "";
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);

        return psi;
    }
}

public class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Id { get; set; } = "";
    public string InterfaceType { get; set; } = "";
    public string MacAddress { get; set; } = "";

    public override string ToString() => DisplayName;
}
