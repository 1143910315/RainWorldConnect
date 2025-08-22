using RainWorldConnect.Network.Data;
using System.Collections.Concurrent;
using System.Net;
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
        private readonly IDispatcherTimer _refreshTimer;
        private readonly PlayerListViewModel playerListViewModel;
        private PlayerData? _myPlayerData;
        public MainPage() {
            InitializeComponent();
            playerListViewModel = (PlayerListViewModel)BindingContext;
            GamePath.Text = Preferences.Get("gamePath", "");
            IsHostCheckBox.IsChecked = Preferences.Get("isHost", false);
            TcpPortEntry.Text = Preferences.Get("tcpPort", "12345");
            UdpPortEntry.Text = Preferences.Get("udpPort", "8720");
            RemoteHostEntry.Text = Preferences.Get("remoteHost", "127.0.0.1:12345");
            deviceId = Preferences.Get("deviceID", Guid.NewGuid().ToString());
            Preferences.Set("deviceID", deviceId);
            _refreshTimer = Dispatcher.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _refreshTimer.Tick += (s, e) => {
                playerListViewModel.PlayerDataList.ForEach(p => {
                    long oldSentBytes = p.LastSentBytes;
                    long newSentBytes = p.TotalSentBytes;
                    p.SentSpeed = FormatSpeed(newSentBytes - oldSentBytes);
                    p.LastSentBytes = newSentBytes;
                    long oldRecvBytes = p.LastReceivedBytes;
                    long newRecvBytes = p.TotalReceivedBytes;
                    p.RevicedSpeed = FormatSpeed(newRecvBytes - oldRecvBytes);
                    p.LastReceivedBytes = newRecvBytes;
                });
            };
            _refreshTimer.Start();
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
                    PlayerData playerData = new() {
                        DeviceId = deviceId,
                        Remark = Preferences.Get("remark_" + deviceId, ""),
                        Port = int.Parse(UdpPortEntry.Text)
                    };
                    _myPlayerData = playerData;
                    int port = int.Parse(TcpPortEntry.Text);
                    playerListViewModel.PlayerDataList.Add(playerData);
                    _tcpService = new();
                    _tcpService.Connected += OnClientConnected;
                    _tcpService.Closed += OnClientClosed;
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
                    _myPlayerData = null;
                    _tcpClient = new();
                    _tcpClient.Connected += OnConnected;
                    _tcpClient.Received += OnDataReceived;
                    _tcpClient.Closed += OnClosed;

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
            playerListViewModel.PlayerDataList.Clear();
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
                                    ForwardPackage forwardPackage = new() {
                                        Port = udpPort,
                                        Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                                    };
                                    using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                                    if (_myPlayerData is PlayerData myPlayerData) {
                                        if (result.RemoteEndPoint is IPEndPoint remoteEndPoint) {
                                            if (myPlayerData.Port != remoteEndPoint.Port) {
                                                BindPortPackage bindPortPackage = new() {
                                                    Port = remoteEndPoint.Port,
                                                };
                                                using ByteBlock bindPortByteBlock = bindPortPackage.ToByteBlock();
                                                myPlayerData.TotalSentBytes += bindPortByteBlock.Length;
                                                await tcpClient.SendAsync(bindPortByteBlock.Memory);
                                            }
                                        }
                                        myPlayerData.TotalSentBytes += forwardByteBlock.Length;
                                    }
                                    if (playerListViewModel.PlayerDataList.FirstOrDefault(p => p.Port == udpPort) is PlayerData targetPlayerData) {
                                        targetPlayerData.TotalSentBytes += forwardByteBlock.Length;
                                    }
                                    await tcpClient.SendAsync(forwardByteBlock.Memory).ConfigureFalseAwait();
                                });
                            }
                        } else if (_tcpService is TcpService tcpService) {
                            if (Application.Current is Application application) {
                                await application.Dispatcher.DispatchAsync(async () => {
                                    if (result.RemoteEndPoint is IPEndPoint remoteEndPoint) {
                                        PlayerData playerData = playerListViewModel.PlayerDataList.First(p => p.DeviceId == deviceId);
                                        PlayerData targetPlayerData = playerListViewModel.PlayerDataList.First(p => p.Port == udpPort);
                                        if (playerData.Port != remoteEndPoint.Port) {
                                        }
                                        ForwardPackage forwardPackage = new() {
                                            Port = remoteEndPoint.Port,
                                            Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                                        };
                                        using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                                        playerData.TotalSentBytes += forwardByteBlock.Length;
                                        targetPlayerData.TotalSentBytes += forwardByteBlock.Length;
                                        await tcpService.SendAsync(targetPlayerData.ClientId, forwardByteBlock.Memory).ConfigureFalseAwait();
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

        private async Task SendAllUserInfoToAllUser() {
            AllUserInfoPackage allUserInfoPackage = new() {
                UserDataList = [.. playerListViewModel.PlayerDataList.Select(p => {
                                    return new UserData{DeviceId = p.DeviceId, Port = p.Port};
                                })]
            };
            using ByteBlock allUserInfoByteBlock = allUserInfoPackage.ToByteBlock();
            if (_tcpService is TcpService tcpService && _myPlayerData is PlayerData myPlayerData) {
                // 创建发送任务列表
                List<Task> sendTasks = [.. playerListViewModel.PlayerDataList.Where(player => player != myPlayerData).Select(player => {
                    player.TotalSentBytes += allUserInfoByteBlock.Length;
                    myPlayerData.TotalReceivedBytes += allUserInfoByteBlock.Length;
                    return tcpService.SendAsync(player.ClientId, allUserInfoByteBlock.Memory);
                })];
                // 并行执行所有发送任务并等待完成
                await Task.WhenAll(sendTasks).ConfigureFalseAwait();
            }
        }

        private async Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e) {
            if (Application.Current is Application application) {
                await application.Dispatcher.DispatchAsync(() => {
                    playerListViewModel.PlayerDataList.Add(new PlayerData {
                        ClientId = client.Id,
                    });
                }).ConfigureFalseAwait();
            }
        }

        private async Task OnClientDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e) {
            using ByteBlock byteBlock = e.ByteBlock;
            DevicePackage devicePackage = new();
            BindPortPackage bindPortPackage = new();
            ForwardPackage forwardPackage = new();
            if (_myPlayerData is PlayerData myPlayerData) {
                myPlayerData.TotalReceivedBytes += byteBlock.Length;
            }
            if (devicePackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        if (_tcpService is TcpService tcpService) {
                            playerListViewModel.PlayerDataList.Where(p => p.DeviceId == devicePackage.DeviceId).ForEach(async p => {
                                if (tcpService.TryGetClient(p.ClientId, out TcpSessionClient client)) {
                                    await client.CloseAsync("踢出服务器");
                                }
                            });
                        }
                        if (playerListViewModel.PlayerDataList.FirstOrDefault(p => p.ClientId == client.Id) is PlayerData playerData) {
                            playerData.DeviceId = devicePackage.DeviceId;
                            playerData.Remark = Preferences.Get("remark_" + devicePackage.DeviceId, "");
                            playerData.TotalReceivedBytes += byteBlock.Length;
                        }
                        await SendAllUserInfoToAllUser().ConfigureFalseAwait();
                    }).ConfigureFalseAwait();
                }
            } else if (bindPortPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        if (playerListViewModel.PlayerDataList.FirstOrDefault(p => p.ClientId == client.Id) is PlayerData playerData) {
                            playerData.Port = bindPortPackage.Port;
                            playerData.TotalReceivedBytes += byteBlock.Length;
                        }
                        if (bindPortPackage.Port != 0 && !udpConnections.ContainsKey(bindPortPackage.Port)) {
                            StartUdpProxy(bindPortPackage.Port);
                        }
                        await SendAllUserInfoToAllUser().ConfigureFalseAwait();
                    }).ConfigureFalseAwait();
                }
            } else if (forwardPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        if (playerListViewModel.PlayerDataList.FirstOrDefault(p => p.ClientId == client.Id) is PlayerData playerData) {
                            playerData.TotalReceivedBytes += byteBlock.Length;
                        }
                        if (playerListViewModel.PlayerDataList.FirstOrDefault(p => p.Port == forwardPackage.Port) is PlayerData targetPlayerData) {
                            string clientID = targetPlayerData.ClientId;
                            IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), forwardPackage.Port);
                            forwardPackage.Port = playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id).Port;
                            if (_tcpService is TcpService tcpService) {
                                if (clientID.IsNullOrWhiteSpace()) {
                                    await udpConnections[forwardPackage.Port].SendToAsync(forwardPackage.Bytes, endpoint).ConfigureFalseAwait();
                                } else {
                                    using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                                    targetPlayerData.TotalSentBytes += forwardByteBlock.Length;
                                    await tcpService.SendAsync(clientID, forwardByteBlock.Memory).ConfigureFalseAwait();
                                }
                            }
                        }
                    }).ConfigureFalseAwait();
                }
            }
        }

        private async Task OnClientClosed(ITcpSessionClient client, ClosedEventArgs e) {
            // 移除断开的客户端
            if (Application.Current is Application application) {
                await application.Dispatcher.DispatchAsync(async () => {
                    playerListViewModel.PlayerDataList.Remove(playerListViewModel.PlayerDataList.First(p => p.ClientId == client.Id));
                    await SendAllUserInfoToAllUser().ConfigureFalseAwait();
                }).ConfigureFalseAwait();
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
            if (_myPlayerData is PlayerData myPlayerData) {
                myPlayerData.TotalReceivedBytes += byteBlock.Length;
            }
            if (allUserInfoPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(() => {
                        Dictionary<string, long> lastReceivedBytesDict = [];
                        Dictionary<string, long> lastSentBytesDict = [];
                        Dictionary<string, long> totalReceivedBytesDict = [];
                        Dictionary<string, long> totalSentBytesDict = [];
                        playerListViewModel.PlayerDataList.ForEach(p => {
                            lastReceivedBytesDict[p.DeviceId] = p.LastReceivedBytes;
                            lastSentBytesDict[p.DeviceId] = p.LastSentBytes;
                            totalReceivedBytesDict[p.DeviceId] = p.TotalReceivedBytes;
                            totalSentBytesDict[p.DeviceId] = p.TotalSentBytes;
                        });
                        playerListViewModel.PlayerDataList.Clear();
                        foreach (UserData userData in allUserInfoPackage.UserDataList) {
                            PlayerData playerData = new() {
                                DeviceId = userData.DeviceId,
                                Remark = Preferences.Get("remark_" + userData.DeviceId, ""),
                                Port = userData.Port,
                                LastReceivedBytes = lastReceivedBytesDict.GetValueOrDefault(userData.DeviceId, 0),
                                LastSentBytes = lastSentBytesDict.GetValueOrDefault(userData.DeviceId, 0),
                                TotalReceivedBytes = totalReceivedBytesDict.GetValueOrDefault(userData.DeviceId, 0),
                                TotalSentBytes = totalSentBytesDict.GetValueOrDefault(userData.DeviceId, 0),
                            };
                            if (userData.DeviceId == deviceId) {
                                _myPlayerData = playerData;
                            }
                            playerListViewModel.PlayerDataList.Add(playerData);
                            if (userData.DeviceId != deviceId && !udpConnections.ContainsKey(userData.Port)) {
                                StartUdpProxy(userData.Port);
                            }
                        }
                    }).ConfigureFalseAwait();
                }
            } else if (forwardPackage.FromByteBlock(byteBlock)) {
                if (Application.Current is Application application) {
                    await application.Dispatcher.DispatchAsync(async () => {
                        PlayerData playerData = playerListViewModel.PlayerDataList.First(p => p.DeviceId == deviceId);
                        IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), playerData.Port);
                        playerData.TotalReceivedBytes += byteBlock.Length;
                        await udpConnections[forwardPackage.Port].SendToAsync(forwardPackage.Bytes, endpoint).ConfigureFalseAwait();
                    }).ConfigureFalseAwait();
                }
            }
        }

        private async Task OnClosed(ITcpClient client, ClosedEventArgs e) {
            if (Application.Current is Application application) {
                await application.Dispatcher.DispatchAsync(() => {
                    playerListViewModel.PlayerDataList.Clear();
                }).ConfigureFalseAwait();
            }
        }

        private async void Button_Clicked_1(object sender, EventArgs e) {
            if (sender is Button button && button.BindingContext is PlayerData playerData && _tcpService is TcpService tcpService) {
                if (tcpService.TryGetClient(playerData.ClientId, out TcpSessionClient client)) {
                    await client.CloseAsync("踢出服务器");
                }
            }
        }

        // 格式化速度值（自动选择单位）
        private static string FormatSpeed(double bytesPerSecond) {
            const double KB = 1024;
            const double MB = KB * 1024;

            return bytesPerSecond switch {
                >= MB => $"{bytesPerSecond / MB:F2} MB/s",
                >= KB => $"{bytesPerSecond / KB:F2} KB/s",
                _ => $"{bytesPerSecond:F0} B/s"
            };
        }
    }
}
