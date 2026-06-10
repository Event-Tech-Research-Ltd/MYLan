using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DhcpFieldServer.Services;

/// <summary>
/// Cross-platform network adapter configuration.
/// Windows: netsh | macOS: networksetup / ifconfig | Linux: ip
/// </summary>
public static class NetworkHelper
{
    public static bool SetStaticIp(string adapterName, string ip, string netmask)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SetStaticIpWindows(adapterName, ip, netmask);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SetStaticIpMac(adapterName, ip, netmask);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return SetStaticIpLinux(adapterName, ip, netmask);

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
                displayName = $"{nic.Name} \u2014 {nic.Description}";
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

    // ─── Windows ──────────────────────────────────────────────

    private static bool SetStaticIpWindows(string adapterName, string ip, string netmask)
    {
        try
        {
            string args = $"interface ip set address \"{adapterName}\" static {ip} {netmask}";
            return RunCommandSuccess("netsh", args);
        }
        catch { return false; }
    }

    // ─── macOS ────────────────────────────────────────────────

    private static bool SetStaticIpMac(string adapterName, string ip, string netmask)
    {
        try
        {
            string? hardwarePort = ResolveMacHardwarePort(adapterName);

            if (hardwarePort != null)
            {
                if (RunCommandSuccess("networksetup", $"-setmanual \"{hardwarePort}\" {ip} {netmask}"))
                    return true;
            }

            // Fallback: ifconfig with BSD name
            string bsdName = adapterName.Contains('\u2014') ? adapterName.Split('\u2014')[0].Trim() : adapterName;
            return RunCommandSuccess("ifconfig", $"{bsdName} {ip} netmask {netmask}");
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

    // ─── Linux ────────────────────────────────────────────────

    private static bool SetStaticIpLinux(string adapterName, string ip, string netmask)
    {
        try
        {
            return RunCommandSuccess("ip", $"addr add {ip}/{NetmaskToCidr(netmask)} dev {adapterName}");
        }
        catch { return false; }
    }

    private static int NetmaskToCidr(string netmask)
    {
        var bytes = System.Net.IPAddress.Parse(netmask).GetAddressBytes();
        int cidr = 0;
        foreach (byte b in bytes)
        {
            byte val = b;
            while (val > 0) { cidr += val & 1; val >>= 1; }
        }
        return cidr;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static bool RunCommandSuccess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName, Arguments = arguments,
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        if (p == null) return false;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return p.ExitCode == 0;
    }

    private static string RunCommand(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName, Arguments = arguments,
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        if (p == null) return "";
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5_000);
        return output;
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
