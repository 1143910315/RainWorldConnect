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
        private readonly ConcurrentDictionary<string, PlayerData> clientIdPlayerDataMap = new();
        private readonly ConcurrentDictionary<string, PlayerData> deviceIdPlayerDataMap = new();
        private readonly ConcurrentDictionary<int, PlayerData> portPlayerDataMap = new();
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
                    long newSentBytes = Interlocked.Read(ref p.totalSentBytes);
                    long oldSentBytes = Interlocked.Exchange(ref p.lastSentBytes, newSentBytes);
                    p.SentSpeed = "↑" + FormatSpeed(newSentBytes - oldSentBytes);
                    long newRecvBytes = Interlocked.Read(ref p.totalReceivedBytes);
                    long oldRecvBytes = Interlocked.Exchange(ref p.lastReceivedBytes, newRecvBytes);
                    p.RevicedSpeed = "↓" + FormatSpeed(newRecvBytes - oldRecvBytes);
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
                        Remark = Preferences.Get("remark_" + deviceId, "")
                    };
                    clientIdPlayerDataMap[""] = playerData;
                    deviceIdPlayerDataMap[deviceId] = playerData;
                    playerData.Port = int.Parse(UdpPortEntry.Text);
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
            clientIdPlayerDataMap.Clear();
            deviceIdPlayerDataMap.Clear();
            portPlayerDataMap.Clear();
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
                            ForwardPackage forwardPackage = new() {
                                Port = udpPort,
                                Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                            };
                            using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                                if (result.RemoteEndPoint is IPEndPoint remoteEndPoint) {
                                    if (myPlayerData.Port != remoteEndPoint.Port) {
                                        BindPortPackage bindPortPackage = new() {
                                            Port = remoteEndPoint.Port,
                                        };
                                        using ByteBlock bindPortByteBlock = bindPortPackage.ToByteBlock();
                                        await tcpClient.SendAsync(bindPortByteBlock.Memory);
                                        Interlocked.Add(ref myPlayerData.totalSentBytes, bindPortByteBlock.Length);
                                    }
                                }
                                await tcpClient.SendAsync(forwardByteBlock.Memory).ConfigureFalseAwait();
                                Interlocked.Add(ref myPlayerData.totalSentBytes, forwardByteBlock.Length);
                                if (portPlayerDataMap.TryGetValue(udpPort, out PlayerData? targetPlayerData)) {
                                    Interlocked.Add(ref targetPlayerData.totalSentBytes, forwardByteBlock.Length);
                                }
                            }
                        } else if (_tcpService is TcpService tcpService) {
                            if (result.RemoteEndPoint is IPEndPoint remoteEndPoint && portPlayerDataMap.TryGetValue(udpPort, out PlayerData? targetPlayerData)) {
                                ForwardPackage forwardPackage = new() {
                                    Port = remoteEndPoint.Port,
                                    Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                                };
                                using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                                await tcpService.SendAsync(targetPlayerData.ClientId, forwardByteBlock.Memory).ConfigureFalseAwait();
                                if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                                    Interlocked.Add(ref myPlayerData.totalSentBytes, forwardByteBlock.Length);
                                }
                                Interlocked.Add(ref targetPlayerData.totalSentBytes, forwardByteBlock.Length);
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
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                AllUserInfoPackage allUserInfoPackage = new() {
                    UserDataList = [.. deviceIdPlayerDataMap.Select(pair => {
                        return new UserData{DeviceId = pair.Value.DeviceId, Port = pair.Value.Port};
                    })]
                };
                using ByteBlock allUserInfoByteBlock = allUserInfoPackage.ToByteBlock();
                if (_tcpService is TcpService tcpService) {
                    // 创建发送任务列表
                    IEnumerable<Task<PlayerData>> sendTasks = clientIdPlayerDataMap.Where(pair => pair.Key != "").Select(pair => Task.Run(async () => {
                        await tcpService.SendAsync(pair.Value.ClientId, allUserInfoByteBlock.Memory);
                        return pair.Value;
                    }));
                    try {
                        // 并行执行所有发送任务并等待完成
                        await Task.WhenAll(sendTasks);
                    } catch (Exception) { }
                    foreach (Task<PlayerData> task in sendTasks) {
                        if (task.IsCompletedSuccessfully) {
                            Interlocked.Add(ref task.Result.totalSentBytes, allUserInfoByteBlock.Length);
                            Interlocked.Add(ref myPlayerData.totalSentBytes, allUserInfoByteBlock.Length);
                        }
                    }
                }
            }
        }

        private Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e) {
            PlayerData playerData = new() {
                ClientId = client.Id,
            };
            clientIdPlayerDataMap[client.Id] = playerData;
            MainThread.BeginInvokeOnMainThread(() => {
                playerListViewModel.PlayerDataList.Add(playerData);
            });
            return EasyTask.CompletedTask;
        }

        private async Task OnClientDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e) {
            using ByteBlock byteBlock = e.ByteBlock;
            DevicePackage devicePackage = new();
            BindPortPackage bindPortPackage = new();
            ForwardPackage forwardPackage = new();
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)
                && clientIdPlayerDataMap.TryGetValue(client.Id, out PlayerData? clientPlayerData)
                && _tcpService is TcpService tcpService) {
                Interlocked.Add(ref myPlayerData.totalReceivedBytes, byteBlock.Length);
                Interlocked.Add(ref clientPlayerData.totalReceivedBytes, byteBlock.Length);
                if (devicePackage.FromByteBlock(byteBlock)) {
                    if (deviceIdPlayerDataMap.TryGetValue(devicePackage.DeviceId, out PlayerData? kickPlayerData)) {
                        if (tcpService.TryGetClient(kickPlayerData.ClientId, out TcpSessionClient kickClient)) {
                            await kickClient.CloseAsync("踢出服务器");
                        }
                    }
                    await MainThread.InvokeOnMainThreadAsync(() => {
                        deviceIdPlayerDataMap.Remove(clientPlayerData.DeviceId, out _);
                        clientPlayerData.DeviceId = devicePackage.DeviceId;
                        deviceIdPlayerDataMap[devicePackage.DeviceId] = clientPlayerData;
                        clientPlayerData.Remark = Preferences.Get("remark_" + devicePackage.DeviceId, "");
                    }).ConfigureFalseAwait();
                    await SendAllUserInfoToAllUser().ConfigureFalseAwait();
                } else if (bindPortPackage.FromByteBlock(byteBlock)) {
                    if (bindPortPackage.Port != 0 && !udpConnections.ContainsKey(bindPortPackage.Port)) {
                        StartUdpProxy(bindPortPackage.Port);
                    }
                    await MainThread.InvokeOnMainThreadAsync(() => {
                        portPlayerDataMap.Remove(clientPlayerData.Port, out _);
                        clientPlayerData.Port = bindPortPackage.Port;
                        portPlayerDataMap[bindPortPackage.Port] = clientPlayerData;
                    }).ConfigureFalseAwait();
                    await SendAllUserInfoToAllUser().ConfigureFalseAwait();
                } else if (forwardPackage.FromByteBlock(byteBlock)) {
                    if (portPlayerDataMap.TryGetValue(forwardPackage.Port, out PlayerData? targetPlayerData)) {
                        string clientID = targetPlayerData.ClientId;
                        IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), forwardPackage.Port);
                        forwardPackage.Port = clientPlayerData.Port;
                        if (clientID.IsNullOrWhiteSpace()) {
                            await udpConnections[clientPlayerData.Port].SendToAsync(forwardPackage.Bytes, endpoint).ConfigureFalseAwait();
                        } else {
                            using ByteBlock forwardByteBlock = forwardPackage.ToByteBlock();
                            Interlocked.Add(ref targetPlayerData.totalSentBytes, forwardByteBlock.Length);
                            await tcpService.SendAsync(clientID, forwardByteBlock.Memory).ConfigureFalseAwait();
                            Interlocked.Add(ref myPlayerData.totalSentBytes, forwardByteBlock.Length);
                        }
                    }
                }
            }
        }

        private async Task OnClientClosed(ITcpSessionClient client, ClosedEventArgs e) {
            // 移除断开的客户端
            await MainThread.InvokeOnMainThreadAsync(async () => {
                if (clientIdPlayerDataMap.TryRemove(client.Id, out PlayerData? clientPlayerData)) {
                    deviceIdPlayerDataMap.Remove(clientPlayerData.DeviceId, out _);
                    portPlayerDataMap.Remove(clientPlayerData.Port, out _);
                    playerListViewModel.PlayerDataList.Remove(clientPlayerData);
                    await SendAllUserInfoToAllUser().ConfigureFalseAwait();
                }
            }).ConfigureFalseAwait();
        }

        private async Task OnConnected(ITcpClient client, ConnectedEventArgs e) {
            PlayerData playerData = new() {
                DeviceId = deviceId,
                Remark = Preferences.Get("remark_" + deviceId, "")
            };
            deviceIdPlayerDataMap[deviceId] = playerData;
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
            if (deviceIdPlayerDataMap.TryGetValue(deviceId, out PlayerData? myPlayerData)) {
                Interlocked.Add(ref myPlayerData.totalReceivedBytes, byteBlock.Length);
                if (allUserInfoPackage.FromByteBlock(byteBlock)) {
                    await MainThread.InvokeOnMainThreadAsync(() => {
                        IEnumerable<PlayerData> newPlayerDataList = allUserInfoPackage.UserDataList.Select(userData =>
                            deviceIdPlayerDataMap.TryGetValue(userData.DeviceId, out PlayerData? playerData)
                                ? playerData
                                : new() {
                                    DeviceId = userData.DeviceId,
                                    Remark = Preferences.Get("remark_" + userData.DeviceId, ""),
                                    Port = userData.Port,
                                }
                        );
                        playerListViewModel.PlayerDataList.Clear();
                        deviceIdPlayerDataMap.Clear();
                        portPlayerDataMap.Clear();
                        foreach (PlayerData p in newPlayerDataList) {
                            playerListViewModel.PlayerDataList.Add(p);
                            deviceIdPlayerDataMap[p.DeviceId] = p;
                            portPlayerDataMap[p.Port] = p;
                            if (p.DeviceId != deviceId && !udpConnections.ContainsKey(p.Port)) {
                                StartUdpProxy(p.Port);
                            }
                        }
                    }).ConfigureFalseAwait();
                } else if (forwardPackage.FromByteBlock(byteBlock)) {
                    if (portPlayerDataMap.TryGetValue(forwardPackage.Port, out PlayerData? targetPlayerData)) {
                        Interlocked.Add(ref targetPlayerData.totalReceivedBytes, byteBlock.Length);
                        IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), myPlayerData.Port);
                        await udpConnections[forwardPackage.Port].SendToAsync(forwardPackage.Bytes, endpoint).ConfigureFalseAwait();
                    }
                }
            }
        }

        private async Task OnClosed(ITcpClient client, ClosedEventArgs e) {
            await MainThread.InvokeOnMainThreadAsync(() => {
                playerListViewModel.PlayerDataList.Clear();
                deviceIdPlayerDataMap.Clear();
                portPlayerDataMap.Clear();
            }).ConfigureFalseAwait();
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
