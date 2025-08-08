using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace RainWorldConnect {
    public partial class MainPage : ContentPage {

        public MainPage() {
            InitializeComponent();
            GamePath.Text = Preferences.Get("gamePath", "");
            IsHostCheckBox.IsChecked = Preferences.Get("isHost", false);
            TcpPortEntry.Text= Preferences.Get("tcpPort", "12345");
            UdpPortEntry.Text = Preferences.Get("udpPort", "8720");
            RemoteHostEntry.Text = Preferences.Get("remoteHost", "127.0.0.1:7777");
            //PlayerListView.BindingContext = new PlayerListViewModel();
        }

        private async void Button_Clicked(object sender, EventArgs e) {
            PickOptions options = new() {
                PickerTitle = "选择雨世界主程序"
            };
            FileResult? fileResult = await FilePicker.Default.PickAsync(options);
            if (fileResult != null) {
                GamePath.Text = fileResult.FullPath;
            }
        }

        private void GamePath_TextChanged(object sender, TextChangedEventArgs e) {
            Preferences.Set("gamePath", GamePath.Text);
        }

        private void TapGestureRecognizer_Tapped(object sender, TappedEventArgs e) {
            IsHostCheckBox.IsChecked = !IsHostCheckBox.IsChecked;
        }

        private void IsHostCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e) {
            TcpPortEntry.IsEnabled = IsHostCheckBox.IsChecked;
            HostLabel.IsEnabled = IsHostCheckBox.IsChecked;
            UdpPortLabel.IsEnabled =IsHostCheckBox.IsChecked;
            UdpPortEntry.IsEnabled = IsHostCheckBox.IsChecked;
            RemoteHostLabel.IsEnabled = !IsHostCheckBox.IsChecked;
            RemoteHostEntry.IsEnabled =!IsHostCheckBox.IsChecked;
            ConfimButton.Text = IsHostCheckBox.IsChecked ? "创建房间" : "加入房间";
            Preferences.Set("isHost", IsHostCheckBox.IsChecked);
        }

        private void TcpPortEntry_TextChanged(object sender, TextChangedEventArgs e) {
             Preferences.Set("tcpPort", TcpPortEntry.Text);
        }

        private static async Task<IPEndPoint> ParseEndpointAsync(string address) {
            if (string.IsNullOrWhiteSpace(address)) {
                throw new ArgumentException("Endpoint cannot be empty");
            }

            // 拆分地址和端口
            int lastColonIndex = address.LastIndexOf(':');
            if (lastColonIndex < 0) {
                throw new FormatException("Invalid endpoint format. Missing port number.");
            }

            string addressPart = address[..lastColonIndex];
            string portPart = address[(lastColonIndex + 1)..];

            // 解析端口
            if (!int.TryParse(portPart, out int port) || port < 0 || port > 65535) {
                throw new FormatException("Invalid port number");
            }

            // 解析 IP 或域名
            if (IPAddress.TryParse(addressPart, out IPAddress? ipAddress)) {
                return new IPEndPoint(ipAddress, port);
            } else {
                // 异步 DNS 解析（支持 MAUI 所有平台）
                IPAddress[] hostEntries = await Dns.GetHostAddressesAsync(addressPart);
                if (hostEntries == null || hostEntries.Length == 0) {
                    throw new SocketException(11001); // HostNotFound
                }

                // 返回第一个 IPv4 地址（可调整策略）
                foreach (var addr in hostEntries) {
                    if (addr.AddressFamily == AddressFamily.InterNetwork) {
                        return new IPEndPoint(addr, port);
                    }
                }

                // 若无 IPv4 则返回第一个地址（IPv6）
                return new IPEndPoint(hostEntries[0], port);
            }
        }

        private void UdpPortEntry_TextChanged(object sender, TextChangedEventArgs e) {
             Preferences.Set("udpPort", UdpPortEntry.Text);
        }

        private void RemoteHostEntry_TextChanged(object sender, TextChangedEventArgs e) {
            Preferences.Set("remoteHost", RemoteHostEntry.Text);
        }

        private void ConfimButton_Clicked(object sender, EventArgs e) {
            if (IsHostCheckBox.IsChecked) {
            } else {
            }
        }
    }
}
