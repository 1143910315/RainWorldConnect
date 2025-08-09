using RainWorldConnect.Network.Data;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using TouchSocket.Core;
using TouchSocket.Sockets;
using TcpClient = TouchSocket.Sockets.TcpClient;

namespace RainWorldConnect {
    public partial class MainPage : ContentPage {
        private CancellationTokenSource _cancellationSource = new();
        private ConcurrentDictionary<int, Socket> udpConnections = new();
        private TcpService? _tcpService;
        private TcpClient? _tcpClient;

        public MainPage() {
            InitializeComponent();
            GamePath.Text = Preferences.Get("gamePath", "");
            IsHostCheckBox.IsChecked = Preferences.Get("isHost", false);
            TcpPortEntry.Text = Preferences.Get("tcpPort", "12345");
            UdpPortEntry.Text = Preferences.Get("udpPort", "8720");
            RemoteHostEntry.Text = Preferences.Get("remoteHost", "127.0.0.1:7777");
            Preferences.Set("deviceID", Preferences.Get("deviceID", Guid.NewGuid().ToString()));
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
            UdpPortLabel.IsEnabled = IsHostCheckBox.IsChecked;
            UdpPortEntry.IsEnabled = IsHostCheckBox.IsChecked;
            RemoteHostLabel.IsEnabled = !IsHostCheckBox.IsChecked;
            RemoteHostEntry.IsEnabled = !IsHostCheckBox.IsChecked;
            ConfimButton.Text = IsHostCheckBox.IsChecked ? "创建房间" : "加入房间";
            Preferences.Set("isHost", IsHostCheckBox.IsChecked);
        }

        private void TcpPortEntry_TextChanged(object sender, TextChangedEventArgs e) {
            Preferences.Set("tcpPort", TcpPortEntry.Text);
        }

        private void UdpPortEntry_TextChanged(object sender, TextChangedEventArgs e) {
            Preferences.Set("udpPort", UdpPortEntry.Text);
        }

        private void RemoteHostEntry_TextChanged(object sender, TextChangedEventArgs e) {
            Preferences.Set("remoteHost", RemoteHostEntry.Text);
        }

        private async void ConfimButton_Clicked(object sender, EventArgs e) {
            await StopProxyAsync();
            if (IsHostCheckBox.IsChecked) {
                int port = int.Parse(TcpPortEntry.Text);
                if (BindingContext is PlayerListViewModel playerListViewModel) {
                    playerListViewModel.PlayerDataList.Add(new PlayerData {
                        DeviceId = Preferences.Get("deviceID", Guid.NewGuid().ToString()),
                        Port = int.Parse(UdpPortEntry.Text)
                    });
                }
                _tcpService = new();
                _tcpService.Connected += OnClientConnected;
                _tcpService.Closed += OnClientDisconnected;
                _tcpService.Received += OnClientDataReceived;
                await _tcpService.SetupAsync(new TouchSocketConfig()
                    .SetListenIPHosts($"0.0.0.0:{port}", $"[::]:{port}")
                    .SetTcpDataHandlingAdapter(() => new FixedHeaderPackageAdapter() {
                        FixedHeaderType = FixedHeaderType.Int
                    })
                    .ConfigureContainer(a => {
                        a.AddEasyLogger((logLevel, obj, loggerString, ex) => { });//添加一个日志注入
                    }));
                await _tcpService.StartAsync();
            } else {
                _tcpClient = new();
                _tcpClient.Connected += OnConnected;
                _tcpClient.Received += OnDataReceived;

                //载入配置
                await _tcpClient.SetupAsync(new TouchSocketConfig()
                     .SetRemoteIPHost(RemoteHostEntry.Text)
                     .SetTcpDataHandlingAdapter(() => new FixedHeaderPackageAdapter() { FixedHeaderType = FixedHeaderType.Int })
                     .ConfigureContainer(a => {
                         a.AddEasyLogger((logLevel, obj, loggerString, ex) => { });//添加一个日志注入
                     })
                     .ConfigurePlugins(a => {
                         a.UseTcpReconnection();// 自动重连
                     }));

                await _tcpClient.ConnectAsync();
            }
        }

        public async Task StartUdpProxy(int udpPort, int udpSocketId) {
            // 清理旧实例
            await StopProxyAsync();

            try {
                _cancellationSource = new CancellationTokenSource();
                CancellationToken token = _cancellationSource.Token;

                // 创建客户端UDP Socket
                Socket _clientSocket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _clientSocket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), udpPort));

                // 启动UDP接收任务
                _ = Task.Run(() => ReceiveUdpData(udpSocketId, token), token);
            } catch (Exception) {
            }
        }

        public async Task StopProxyAsync() {
            if (_tcpService is TcpService tcpService) {
                await tcpService.StopAsync();
            }
            if (_tcpClient is TcpClient tcpClient) {
                await tcpClient.CloseAsync();
            }
            _tcpService = null;
            _tcpClient = null;
            if (BindingContext is PlayerListViewModel playerListViewModel) {
                playerListViewModel.PlayerDataList.Clear();
            }
        }

        private async Task ReceiveUdpData(int udpSocketId, CancellationToken token) {
            try {
                byte[] buffer = new byte[65507 + 4]; // UDP最大包大小 + 四字节长度
                IPEndPoint endpoint = new(IPAddress.Any, 0);

                Socket udpSocket = udpConnections[udpSocketId];

                while (!token.IsCancellationRequested) {
                    try {
                        SocketReceiveFromResult result = await udpSocket.ReceiveFromAsync(buffer.AsMemory(9), SocketFlags.None, endpoint, token);
                        if (result.ReceivedBytes == 0) {
                            continue;
                        }

                        // 添加4字节长度头
                        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), result.ReceivedBytes);

                        // 添加1字节包类型
                        buffer[4] = 0; // 0: 转发数据包

                        // 添加4字节长度头
                        BitConverter.TryWriteBytes(buffer.AsSpan(5, 4), udpSocketId);

                    } catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) {
                    }
                }
            } catch (OperationCanceledException) {
                /* 正常退出 */
            } catch (Exception) {
            }
        }
        private Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e) {
            if (BindingContext is PlayerListViewModel playerListViewModel) {
                Application.Current?.Dispatcher.Dispatch(() => {
                    playerListViewModel.PlayerDataList.Add(new PlayerData { ClientId = client.Id });
                });
            }
            // 等待客户端发送ID注册
            return EasyTask.CompletedTask;
        }

        private Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e) {
            // 移除断开的客户端
            if (BindingContext is PlayerListViewModel playerListViewModel) {
                Application.Current?.Dispatcher.Dispatch(() => {
                    playerListViewModel.PlayerDataList.Remove(playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id));
                });
            }
            return EasyTask.CompletedTask;
        }

        private async Task OnClientDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e) {
            using ByteBlock byteBlock = e.ByteBlock;
            DevicePackage devicePackage = new();
            if (devicePackage.FromByteBlock(byteBlock)) {
                if (BindingContext is PlayerListViewModel playerListViewModel) {
                    playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id).DeviceId = devicePackage.DeviceId;
                    AllUserInfoPackage allUserInfoPackage = new() {
                        UserDataList = [.. playerListViewModel.PlayerDataList.Select(p => {
                            return new UserData {DeviceId = p.DeviceId, Port = p.Port };
                        })]
                    };
                    using ByteBlock allUserInfoByteBlock = allUserInfoPackage.ToByteBlock();
                    await client.SendAsync(allUserInfoByteBlock.Memory);
                }
            }
        }

        private async Task OnConnected(ITcpClient client, ConnectedEventArgs e) {
            // 发送注册消息
            DevicePackage deviceInformationPackage = new() {
                DeviceId = Preferences.Get("deviceID", Guid.NewGuid().ToString())
            };
            using ByteBlock byteBlock = deviceInformationPackage.ToByteBlock();
            await client.SendAsync(byteBlock.Memory);
        }

        private Task OnDataReceived(ITcpClient client, ReceivedDataEventArgs e) {
            using ByteBlock byteBlock = e.ByteBlock;
            AllUserInfoPackage allUserInfoPackage = new();
            if (allUserInfoPackage.FromByteBlock(byteBlock)) {
                if (BindingContext is PlayerListViewModel playerListViewModel) {
                    Application.Current?.Dispatcher.Dispatch(() => {
                        playerListViewModel.PlayerDataList.Clear();
                        foreach (UserData userData in allUserInfoPackage.UserDataList) {
                            playerListViewModel.PlayerDataList.Add(new PlayerData {
                                DeviceId = userData.DeviceId,
                                Remark = Preferences.Get("remark_" + userData.DeviceId, ""),
                                Port = userData.Port
                            });

                        }
                    });
                }
            }
            return Task.CompletedTask;
        }
    }
}
