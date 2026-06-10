using System;
using System.Diagnostics;

namespace DhcpFieldServer
{
    public static class NetworkHelper
    {
        public static bool SetStaticIp(string adapterName, string ip, string netmask)
        {
            try
            {
                // netsh interface ip set address "Ethernet 2" static 192.168.1.1 255.255.255.0
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"interface ip set address \"{adapterName}\" static {ip} {netmask}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;

                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();

                p.WaitForExit();

                // For debugging, you could log output/error if needed
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
