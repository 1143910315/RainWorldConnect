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
        private readonly ConcurrentDictionary<int, Socket> udpConnections = new();
        private TcpService? _tcpService;
        private TcpClient? _tcpClient;
        private readonly string deviceId = "";

        public MainPage() {
            InitializeComponent();
            GamePath.Text = Preferences.Get("gamePath", "");
            IsHostCheckBox.IsChecked = Preferences.Get("isHost", false);
            TcpPortEntry.Text = Preferences.Get("tcpPort", "12345");
            UdpPortEntry.Text = Preferences.Get("udpPort", "8720");
            RemoteHostEntry.Text = Preferences.Get("remoteHost", "127.0.0.1:12345");
            deviceId = Preferences.Get("deviceID", Guid.NewGuid().ToString());
            Preferences.Set("deviceID", deviceId);
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
            try {
                await StopProxyAsync();
                _cancellationSource = new CancellationTokenSource();
                if (IsHostCheckBox.IsChecked) {
                    int port = int.Parse(TcpPortEntry.Text);
                    if (BindingContext is PlayerListViewModel playerListViewModel) {
                        playerListViewModel.PlayerDataList.Add(new PlayerData {
                            DeviceId = deviceId,
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
                    try {
                        await _tcpService.StartAsync();
                    } catch (Exception ex) {
                        await DisplayAlert("错误", $"尝试在{port}端口启动服务器时出现问题，疑似端口占用，更换端口可能解决问题。\n发生异常: {ex.Message}", "确定");
                        await StopProxyAsync();
                    }
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
                    try {
                        await _tcpClient.ConnectAsync();
                    } catch (Exception ex) {
                        await DisplayAlert("错误", $"尝试连接到{RemoteHostEntry.Text}时出现问题，疑似地址错误、端口错误或者无网络，无法连接。\n发生异常: {ex.Message}", "确定");
                        await StopProxyAsync();
                    }
                }
            } catch (Exception ex) {
                await DisplayAlert("错误", $"发生异常: {ex.Message}", "确定");
                await StopProxyAsync();
            }
        }

        public void StartUdpProxy(int udpPort) {
            try {
                CancellationToken token = _cancellationSource.Token;

                // 创建客户端UDP Socket
                Socket _clientSocket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _clientSocket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), udpPort));
                udpConnections[udpPort] = _clientSocket;
                // 启动UDP接收任务
                _ = Task.Run(() => ReceiveUdpData(udpPort, token), token).ConfigureFalseAwait();
            } catch (Exception) {
            }
        }

        public async Task StopProxyAsync() {
            if (_tcpService is TcpService tcpService) {
                await tcpService.StopAsync().ConfigureFalseAwait();
            }
            if (_tcpClient is TcpClient tcpClient) {
                await tcpClient.CloseAsync().ConfigureFalseAwait();
            }
            _tcpService = null;
            _tcpClient = null;
            _cancellationSource?.Cancel();
            foreach ((int _, Socket v) in udpConnections) {
                v.Close();
            }
            udpConnections.Clear();
            if (BindingContext is PlayerListViewModel playerListViewModel) {
                playerListViewModel.PlayerDataList.Clear();
            }
        }

        private async Task ReceiveUdpData(int udpPort, CancellationToken token) {
            try {
                byte[] buffer = new byte[65507]; // UDP最大包大小 + 四字节长度
                IPEndPoint endpoint = new(IPAddress.Any, 0);

                Socket udpSocket = udpConnections[udpPort];

                while (!token.IsCancellationRequested) {
                    try {
                        SocketReceiveFromResult result = await udpSocket.ReceiveFromAsync(buffer, SocketFlags.None, endpoint, token);
                        if (result.ReceivedBytes == 0) {
                            continue;
                        }
                        if (_tcpClient is TcpClient tcpClient) {
                            if (Application.Current is Application application) {
                                await application.Dispatcher.DispatchAsync(async () => {
                                    if (BindingContext is PlayerListViewModel playerListViewModel) {
                                        if (result.RemoteEndPoint is IPEndPoint remoteEndPoint) {
                                            if (playerListViewModel.PlayerDataList.First(p => p.DeviceId == deviceId).Port != remoteEndPoint.Port) {
                                                BindPortPackage bindPortPackage = new() {
                                                    Port = remoteEndPoint.Port,
                                                };
                                                using ByteBlock bindPortByteBlock = bindPortPackage.ToByteBlock();
                                                await tcpClient.SendAsync(bindPortByteBlock.Memory).ConfigureFalseAwait();
                                            }
                                        }

                                    }
                                });
                            }
                            ForwardPackage forwardPackage = new() {
                                Port = udpPort,
                                Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                            };
                            using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                            await tcpClient.SendAsync(forwardByteBlock.Memory).ConfigureFalseAwait();
                        } else if (_tcpService is TcpService tcpService) {
                            if (Application.Current is Application application) {
                                await application.Dispatcher.DispatchAsync(async () => {
                                    if (BindingContext is PlayerListViewModel playerListViewModel) {
                                        if (result.RemoteEndPoint is IPEndPoint remoteEndPoint) {
                                            PlayerData playerData = playerListViewModel.PlayerDataList.First(p => p.ClientId == "");
                                            PlayerData targetPlayerData = playerListViewModel.PlayerDataList.First(p => p.Port == udpPort);
                                            if (playerData.Port != remoteEndPoint.Port) {
                                            }
                                            ForwardPackage forwardPackage = new() {
                                                Port = remoteEndPoint.Port,
                                                Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                                            };
                                            using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                                            await tcpService.SendAsync(targetPlayerData.ClientId, forwardByteBlock.Memory).ConfigureFalseAwait();
                                        }
                                    }
                                }).ConfigureFalseAwait();
                            }
                        }
                    } catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) {
                    }
                }
            } catch (OperationCanceledException) {
                /* 正常退出 */
            } catch (Exception) {
            }
        }

        private async Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e) {
            if (Application.Current is Application application) {
                await application.Dispatcher.DispatchAsync(() => {
                    if (BindingContext is PlayerListViewModel playerListViewModel) {
                        playerListViewModel.PlayerDataList.Add(new PlayerData { ClientId = client.Id });
                    }
                }).ConfigureFalseAwait();
            }
        }

        private async Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e) {
            // 移除断开的客户端
            if (Application.Current is Application application) {
                await application.Dispatcher.DispatchAsync(async () => {
                    if (BindingContext is PlayerListViewModel playerListViewModel) {
                        playerListViewModel.PlayerDataList.Remove(playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id));
                        AllUserInfoPackage allUserInfoPackage = new() {
                            UserDataList = [.. playerListViewModel.PlayerDataList.Select(p => {
                                    return new UserData{DeviceId = p.DeviceId, Port = p.Port};
                                })]
                        };
                        using ByteBlock allUserInfoByteBlock = allUserInfoPackage.ToByteBlock();
                        if (_tcpService is TcpService tcpService) {
                            foreach (var player in playerListViewModel.PlayerDataList) {
                                if (!player.ClientId.IsNullOrWhiteSpace()) {
                                    await tcpService.SendAsync(player.ClientId, allUserInfoByteBlock.Memory);
                                }
                            }
                        }
                    }
                }).ConfigureFalseAwait();
            }
        }

        private async Task OnClientDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e) {
            using ByteBlock byteBlock = e.ByteBlock;
            DevicePackage devicePackage = new();
            BindPortPackage bindPortPackage = new();
            ForwardPackage forwardPackage = new();
            if (devicePackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        if (BindingContext is PlayerListViewModel playerListViewModel) {
                            playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id).DeviceId = devicePackage.DeviceId;
                            AllUserInfoPackage allUserInfoPackage = new() {
                                UserDataList = [.. playerListViewModel.PlayerDataList.Select(p => {
                                    return new UserData{DeviceId = p.DeviceId, Port = p.Port};
                                })]
                            };
                            using ByteBlock allUserInfoByteBlock = allUserInfoPackage.ToByteBlock();
                            if (_tcpService is TcpService tcpService) {
                                foreach (var player in playerListViewModel.PlayerDataList) {
                                    if (!player.ClientId.IsNullOrWhiteSpace()) {
                                        await tcpService.SendAsync(player.ClientId, allUserInfoByteBlock.Memory);
                                    }
                                }
                            }
                        }
                    }).ConfigureFalseAwait();
                }
            } else if (bindPortPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        if (BindingContext is PlayerListViewModel playerListViewModel) {
                            playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id).Port = bindPortPackage.Port;
                            if (bindPortPackage.Port != 0 && !udpConnections.ContainsKey(bindPortPackage.Port)) {
                                StartUdpProxy(bindPortPackage.Port);
                            }
                            AllUserInfoPackage allUserInfoPackage = new() {
                                UserDataList = [.. playerListViewModel.PlayerDataList.Select(p => {
                                    return new UserData{DeviceId = p.DeviceId, Port = p.Port};
                                })]
                            };
                            using ByteBlock allUserInfoByteBlock = allUserInfoPackage.ToByteBlock();
                            if (_tcpService is TcpService tcpService) {
                                foreach (var player in playerListViewModel.PlayerDataList) {
                                    if (!player.ClientId.IsNullOrWhiteSpace()) {
                                        await tcpService.SendAsync(player.ClientId, allUserInfoByteBlock.Memory);
                                    }
                                }
                            }
                        }
                    }).ConfigureFalseAwait();
                }
            } else if (forwardPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        if (BindingContext is PlayerListViewModel playerListViewModel) {
                            string clientID = playerListViewModel.PlayerDataList.First(p => p.Port == forwardPackage.Port).ClientId;
                            IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), forwardPackage.Port);
                            forwardPackage.Port = playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id).Port;
                            if (_tcpService is TcpService tcpService) {
                                if (clientID.IsNullOrWhiteSpace()) {
                                    await udpConnections[forwardPackage.Port].SendToAsync(forwardPackage.Bytes, endpoint).ConfigureFalseAwait();
                                } else {
                                    using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                                    await tcpService.SendAsync(clientID, forwardByteBlock.Memory).ConfigureFalseAwait();
                                }

                            }
                        }
                    }).ConfigureFalseAwait();
                }
            }
        }

        private async Task OnConnected(ITcpClient client, ConnectedEventArgs e) {
            // 发送注册消息
            DevicePackage deviceInformationPackage = new() {
                DeviceId = deviceId
            };
            using ByteBlock byteBlock = deviceInformationPackage.ToByteBlock();
            await client.SendAsync(byteBlock.Memory).ConfigureFalseAwait();
        }

        private async Task OnDataReceived(ITcpClient client, ReceivedDataEventArgs e) {
            using ByteBlock byteBlock = e.ByteBlock;
            AllUserInfoPackage allUserInfoPackage = new();
            ForwardPackage forwardPackage = new();
            if (allUserInfoPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(() => {
                        if (BindingContext is PlayerListViewModel playerListViewModel) {
                            playerListViewModel.PlayerDataList.Clear();
                            foreach (UserData userData in allUserInfoPackage.UserDataList) {
                                playerListViewModel.PlayerDataList.Add(new PlayerData {
                                    DeviceId = userData.DeviceId,
                                    Remark = Preferences.Get("remark_" + userData.DeviceId, ""),
                                    Port = userData.Port
                                });
                                if (userData.DeviceId != deviceId && !udpConnections.ContainsKey(userData.Port)) {
                                    StartUdpProxy(userData.Port);
                                }
                            }
                        }
                    }).ConfigureFalseAwait();
                }
            } else if (forwardPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        if (BindingContext is PlayerListViewModel playerListViewModel) {
                            IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), playerListViewModel.PlayerDataList.First(p => p.DeviceId == deviceId).Port);
                            await udpConnections[forwardPackage.Port].SendToAsync(forwardPackage.Bytes, endpoint).ConfigureFalseAwait();
                        }
                    }).ConfigureFalseAwait();
                }
            }
        }
    }
}
