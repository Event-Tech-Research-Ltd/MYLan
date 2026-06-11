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

    public static List<NetworkAdapterInfo> GetAvailableAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n =>
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                n.OperationalStatus == OperationalStatus.Up);

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
            return RunCommandSuccess("networksetup", "-setdhcp", port);

        if (string.IsNullOrWhiteSpace(config.IpAddress) || string.IsNullOrWhiteSpace(config.SubnetMask))
            return false;

        string router = string.IsNullOrWhiteSpace(config.Gateway) ? config.IpAddress : config.Gateway;
        return RunCommandSuccess("networksetup", "-setmanual", port, config.IpAddress, config.SubnetMask, router);
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
            string? hardwarePort = ResolveMacHardwarePort(adapterName);

            if (hardwarePort != null)
            {
                string router = string.IsNullOrWhiteSpace(gateway) ? ip : gateway;
                if (RunCommandSuccess("networksetup", "-setmanual", hardwarePort, ip, netmask, router))
                    return true;
            }

            string bsdName = adapterName.Contains(" - ", StringComparison.Ordinal)
                ? adapterName.Split(" - ", StringSplitOptions.None)[0].Trim()
                : adapterName;
            return RunCommandSuccess("ifconfig", bsdName, ip, "netmask", netmask);
        }
        catch { return false; }
    }

    private static string? ResolveMacHardwarePort(string nameOrBsd)
    {
        try
        {
            string output = RunCommand("networksetup", "-listallhardwareports");
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
