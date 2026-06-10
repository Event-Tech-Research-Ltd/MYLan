using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Media;

namespace DhcpFieldServer
{
    public partial class MainWindow : Window
    {
        private DhcpServer? _server;

        private static readonly string LeaseFilePath = GetWritableLeaseFilePath();

        private static string GetWritableLeaseFilePath()
        {
            string primary = Path.Combine(AppContext.BaseDirectory, "mylan-leases.json");
            try
            {
                string testFile = primary + ".writetest";
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                return primary;
            }
            catch
            {
                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MYLan", "mylan-leases.json");
                Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
                return fallback;
            }
        }

        private static readonly int MaxLogLines = 2000;

        public MainWindow()
        {
            InitializeComponent();
            LoadAdapters();
        }

        private void LoadAdapters()
        {
            ServerNicCombo.Items.Clear();

            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n =>
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    n.OperationalStatus == OperationalStatus.Up);

            foreach (var nic in nics)
                ServerNicCombo.Items.Add(nic.Name);

            if (ServerNicCombo.Items.Count > 0)
                ServerNicCombo.SelectedIndex = 0;
        }

        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ServerNicCombo.SelectedItem == null)
            {
                MessageBox.Show("Select a server network adapter first.", "MYLan",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string adapterName = ServerNicCombo.SelectedItem.ToString()!;

            AppendLog($"Using adapter: {adapterName}");
            AppendLog("Setting static IP 192.168.1.1/255.255.255.0 on adapter...");

            bool ipOk = NetworkHelper.SetStaticIp(adapterName, "192.168.1.1", "255.255.255.0");
            if (!ipOk)
            {
                AppendLog("[ERROR] Failed to set static IP. Are you running as Administrator?");
                MessageBox.Show("Failed to set static IP. Run this app as Administrator.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AppendLog("Static IP configured successfully.");

            try
            {
                _server = new DhcpServer(
                    serverIpAddress: "192.168.1.1",
                    poolStart: "192.168.1.50",
                    poolEnd: "192.168.1.150",
                    subnetMask: "255.255.255.0",
                    routerIp: "192.168.1.1",
                    dnsIp: "8.8.8.8",
                    leaseFilePath: LeaseFilePath);

                _server.Log += msg =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        AppendLog(msg);
                        RefreshLeases();
                    });
                };

                _server.Start();

                SetStatus("DHCP Running", "#4CAF50");
                StartBtn.IsEnabled = false;
                StopBtn.IsEnabled = true;
                ServerNicCombo.IsEnabled = false;

                // Show restored lease count
                int restored = _server.ActiveLeases.Count;
                if (restored > 0)
                    AppendLog($"{restored} lease(s) restored from previous session.");

                RefreshLeases();
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] " + ex.Message);
                MessageBox.Show("Failed to start DHCP server:\n" + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            try { _server?.Stop(); }
            catch (Exception ex) { AppendLog("[ERROR] " + ex.Message); }

            SetStatus("Stopped", "#F44336");
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            ServerNicCombo.IsEnabled = true;
            _server = null;
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadAdapters();
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Clear();
        }

        private void RefreshLeases()
        {
            if (_server == null) return;

            var items = _server.ActiveLeases.Select(l => new
            {
                l.Mac,
                l.IpAddress,
                Expiry = l.ExpiryUtc.ToLocalTime().ToString("HH:mm:ss")
            }).ToList();

            LeaseGrid.ItemsSource = items;
        }

        private void SetStatus(string text, string hexColour)
        {
            StatusText.Text = text;
            var colour = (Color)ColorConverter.ConvertFromString(hexColour);
            StatusDot.Fill = new SolidColorBrush(colour);
        }

        private void AppendLog(string message)
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

            // Cap log to prevent unbounded memory growth during long sessions
            if (LogBox.LineCount > MaxLogLines)
            {
                int trimTo = LogBox.GetCharacterIndexFromLineIndex(LogBox.LineCount - MaxLogLines);
                LogBox.Text = LogBox.Text.Substring(trimTo);
            }

            LogBox.ScrollToEnd();
        }
    }
}
