using RainWorldConnect.Data;
using RainWorldConnect.Network.Adapter;
using RainWorldConnect.Network.Base;
using RainWorldConnect.Network.Package;
using RainWorldConnect.Network.Plugin;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using TouchSocket.Core;
using TouchSocket.Sockets;
using TcpClient = TouchSocket.Sockets.TcpClient;

namespace RainWorldConnect {
    public partial class MainPage : ContentPage {
        private CancellationTokenSource _cancellationSource = new();
        private readonly ConcurrentDictionary<int, Socket> udpConnections = new();
        private TcpService? _tcpService;
        private TcpClient? _tcpClient;
        private readonly IDispatcherTimer _refreshTimer;
        private readonly PlayerListViewModel playerListViewModel;
        private readonly ConcurrentDictionary<string, PlayerData> clientIdPlayerDataMap = new();
        private readonly ConcurrentDictionary<string, PlayerData> deviceIdPlayerDataMap = new();
        private readonly ConcurrentDictionary<int, PlayerData> portPlayerDataMap = new();
        private readonly List<string> serviceIdList = [];
        private string serviceId = "";
        private readonly Lock keyLock = new();
        public MainPage() {
            InitializeComponent();
            playerListViewModel = (PlayerListViewModel)BindingContext;
            GamePath.Text = Preferences.Get("gamePath", "");
            IsHostCheckBox.IsChecked = Preferences.Get("isHost", false);
            TcpPortEntry.Text = Preferences.Get("tcpPort", "12345");
            UdpPortEntry.Text = Preferences.Get("udpPort", "8720");
            RemoteHostEntry.Text = Preferences.Get("remoteHost", "127.0.0.1:12345");
            string serviceIdJson = Preferences.Get("serviceIdJson", "[\"" + Guid.NewGuid().ToString() + "\"]");
            serviceIdList = JsonSerializer.Deserialize<List<string>>(serviceIdJson) ?? [Guid.NewGuid().ToString()];
            Preferences.Set("serviceIdJson", JsonSerializer.Serialize(serviceIdList));
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

        private PackageHandlingAdapter CreatePackageHandlingAdapter() {
            return new PackageHandlingAdapter()
                  .Register<AllUserInfoPackage>()
                  .Register<AuthenticationIdPackage>()
                  .Register<BindPortPackage>()
                  .Register<ConfirmRegisterPackage>()
                  .Register<DeviceIdPackage>()
                  .Register<ForwardPackage>()
                  .Register<ServiceIdPackage>()
                  .Register<SwitchServiceIdPackage>();
        }

        private async void ConfimButton_Clicked(object sender, EventArgs e) {
            try {
                await StopProxyAsync();
                _cancellationSource = new CancellationTokenSource();
                PlayerData playerData = new();
                clientIdPlayerDataMap[""] = playerData;
                if (IsHostCheckBox.IsChecked) {
                    int port = int.Parse(TcpPortEntry.Text);
                    serviceId = serviceIdList[0];
                    playerData.DeviceId = "A";
                    playerData.Port = int.Parse(UdpPortEntry.Text);
                    portPlayerDataMap[playerData.Port] = playerData;
                    deviceIdPlayerDataMap["A"] = playerData;
                    playerListViewModel.PlayerDataList.Add(playerData);
                    _tcpService = new();
                    _tcpService.Connected += OnClientConnected;
                    _tcpService.Closed += OnClientClosed;
                    await _tcpService.SetupAsync(new TouchSocketConfig()
                        .SetListenIPHosts($"0.0.0.0:{port}", $"[::]:{port}")
                        .SetTcpDataHandlingAdapter(CreatePackageHandlingAdapter)
                        .ConfigurePlugins(a => {
                            a.Add(new TcpServiceReceivedPlugin<BinaryPackageBase, TcpService>(PackageServiceHandler, _tcpService));
                            a.Add(new TcpServiceReceivedPlugin<BindPortPackage, TcpService>(BindPortPackageServiceHandler, _tcpService));
                            a.Add(new TcpServiceReceivedPlugin<ForwardPackage, TcpService>(ForwardPackageServiceHandler, _tcpService));
                            a.Add(new TcpServiceReceivedPlugin<AuthenticationIdPackage, TcpService>(AuthenticationIdPackageServiceHandler, _tcpService));
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
                    _tcpClient.Closed += OnClosed;
                    //载入配置
                    await _tcpClient.SetupAsync(new TouchSocketConfig()
                         .SetRemoteIPHost(RemoteHostEntry.Text)
                         .SetTcpDataHandlingAdapter(CreatePackageHandlingAdapter)
                         .ConfigurePlugins(a => {
                             a.UseReconnection<TcpClient>();// 自动重连
                             a.Add(new TcpClientReceivedPlugin<BinaryPackageBase, TcpClient>(PackageClientHandler, _tcpClient));
                             a.Add(new TcpClientReceivedPlugin<AllUserInfoPackage, TcpClient>(AllUserInfoPackageClientHandler, _tcpClient));
                             a.Add(new TcpClientReceivedPlugin<ForwardPackage, TcpClient>(ForwardPackageClientHandler, _tcpClient));
                             a.Add(new TcpClientReceivedPlugin<ServiceIdPackage, TcpClient>(ServiceIdPackageClientHandler, _tcpClient));
                             a.Add(new TcpClientReceivedPlugin<ConfirmRegisterPackage, TcpClient>(ConfirmRegisterPackageClientHandler, _tcpClient));
                         })
                         .ConfigureContainer(a => {
                             a.AddEasyLogger((logLevel, obj, loggerString, ex) => { });//添加一个日志注入
                         })
                    );
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

        private Task<bool> ConfirmRegisterPackageClientHandler(ConfirmRegisterPackage package, TcpClient client) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                // 保存认证ID和设备ID到本地存储
                Preferences.Set($"authorized_{serviceId}", package.AuthenticationId);
                Preferences.Set($"device_{serviceId}", package.DeviceId);

                UpdatePlayerDeviceId(myPlayerData, package.DeviceId); // 使用新函数封装修改逻辑
            }
            return Task.FromResult(true);
        }

        private async Task<bool> ServiceIdPackageClientHandler(ServiceIdPackage package, TcpClient client) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                serviceId = package.ServiceId;
                var authorized = Preferences.Get($"authorized_{package.ServiceId}", "");
                var deviced = Preferences.Get($"device_{package.ServiceId}", "");
                if (authorized != "" && deviced != "") {
                    using var asymmetricCryptoHelper = new AsymmetricCryptoHelper();
                    asymmetricCryptoHelper.ImportPublicKeyFromPem(package.PublicKey);
                    authorized = asymmetricCryptoHelper.Encrypt(authorized);
                    UpdatePlayerDeviceId(myPlayerData, deviced); // 使用新函数替换直接修改
                } else {
                    authorized = "";
                    deviced = "";
                }
                AuthenticationIdPackage authenticationIdPackage = new() {
                    Index = package.Index,
                    DeviceId = deviced,
                    AuthenticationId = authorized
                };
                await client.SendAsync(authenticationIdPackage);
                Interlocked.Add(ref myPlayerData.totalSentBytes, authenticationIdPackage.Length);
            }
            return true;
        }

        private async Task<bool> ForwardPackageClientHandler(ForwardPackage package, TcpClient client) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)
                && portPlayerDataMap.TryGetValue(package.Port, out PlayerData? targetPlayerData)) {
                Interlocked.Add(ref targetPlayerData.totalReceivedBytes, package.Length);
                IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), myPlayerData.Port);
                await udpConnections[package.Port].SendToAsync(package.Bytes, endpoint).ConfigureFalseAwait();
            }
            return true;
        }

        private Task<bool> PackageClientHandler(BinaryPackageBase package, TcpClient client) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                Interlocked.Add(ref myPlayerData.totalReceivedBytes, package.Length);
            }
            return Task.FromResult(false);
        }

        private async Task<bool> AllUserInfoPackageClientHandler(AllUserInfoPackage package, TcpClient client) {
            await MainThread.InvokeOnMainThreadAsync(() => {
                if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                    var newPlayerDataList = package.UserDataList.Select(userData => {
                        if (deviceIdPlayerDataMap.TryGetValue(userData.DeviceId, out PlayerData? playerData)) {
                            playerData.Port = userData.Port;
                            return playerData;
                        } else {
                            return new PlayerData() {
                                DeviceId = userData.DeviceId,
                                Remark = Preferences.Get("remark_" + userData.DeviceId, ""),
                                Port = userData.Port,
                            };
                        }
                    }).ToList();
                    playerListViewModel.PlayerDataList.Clear();
                    deviceIdPlayerDataMap.Clear();
                    portPlayerDataMap.Clear();
                    foreach (PlayerData p in newPlayerDataList) {
                        playerListViewModel.PlayerDataList.Add(p);
                        deviceIdPlayerDataMap[p.DeviceId] = p;
                        if (p.Port != 0) {
                            portPlayerDataMap[p.Port] = p;
                            if (p != myPlayerData && !udpConnections.ContainsKey(p.Port)) {
                                StartUdpProxy(p.Port);
                            }
                        }
                    }
                }
            }).ConfigureFalseAwait();
            return true;
        }

        private Task<bool> PackageServiceHandler(ITcpSessionClient client, BinaryPackageBase package, TcpService service) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                Interlocked.Add(ref myPlayerData.totalReceivedBytes, package.Length);
            }
            if (clientIdPlayerDataMap.TryGetValue(client.Id, out PlayerData? clientPlayerData)) {
                Interlocked.Add(ref clientPlayerData.totalReceivedBytes, package.Length);
            }
            return Task.FromResult(false);
        }

        private async Task<bool> BindPortPackageServiceHandler(ITcpSessionClient client, BindPortPackage package, TcpService service) {
            if (clientIdPlayerDataMap.TryGetValue(client.Id, out PlayerData? clientPlayerData)) {
                if (package.Port != 0 && !udpConnections.ContainsKey(package.Port)) {
                    StartUdpProxy(package.Port);
                }
                await MainThread.InvokeOnMainThreadAsync(() => {
                    portPlayerDataMap.Remove(clientPlayerData.Port, out _);
                    clientPlayerData.Port = package.Port;
                    portPlayerDataMap[package.Port] = clientPlayerData;
                }).ConfigureFalseAwait();
                SendAllUserInfoToAllUser();
            }
            return true;
        }

        private async Task<bool> ForwardPackageServiceHandler(ITcpSessionClient client, ForwardPackage package, TcpService service) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)
                && clientIdPlayerDataMap.TryGetValue(client.Id, out PlayerData? clientPlayerData)) {
                if (portPlayerDataMap.TryGetValue(package.Port, out PlayerData? targetPlayerData)) {
                    string clientID = targetPlayerData.ClientId;
                    IPEndPoint endpoint = new(IPAddress.Parse("127.0.0.1"), package.Port);
                    package.Port = clientPlayerData.Port;
                    if (clientID.IsNullOrWhiteSpace()) {
                        await udpConnections[clientPlayerData.Port].SendToAsync(package.Bytes, endpoint).ConfigureFalseAwait();
                    } else {
                        await service.SendAsync(clientID, package).ConfigureFalseAwait();
                        Interlocked.Add(ref targetPlayerData.totalSentBytes, package.Length);
                        Interlocked.Add(ref myPlayerData.totalSentBytes, package.Length);
                    }
                }
            }
            return true;
        }

        private async Task<bool> AuthenticationIdPackageServiceHandler(ITcpSessionClient client, AuthenticationIdPackage package, TcpService service) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)
                && clientIdPlayerDataMap.TryGetValue(client.Id, out PlayerData? clientPlayerData)) {
                var authenticationId = package.AuthenticationId;
                var deviceId = package.DeviceId;
                if (authenticationId == "" || deviceId == "") {
                    var nextDeviceId = AdvancedStringIncrementer.IncrementString(Preferences.Get("max_generated_device_id", "A"));
                    authenticationId = Guid.NewGuid().ToString();
                    Preferences.Set("authentication_" + nextDeviceId, authenticationId);
                    Preferences.Set("max_generated_device_id", nextDeviceId);
                    var confirmRegisterPackage = new ConfirmRegisterPackage { AuthenticationId = authenticationId, DeviceId = nextDeviceId };
                    await client.SendAsync(confirmRegisterPackage);
                    Interlocked.Add(ref myPlayerData.totalSentBytes, confirmRegisterPackage.Length);
                    Interlocked.Add(ref clientPlayerData.totalSentBytes, confirmRegisterPackage.Length);
                    UpdatePlayerDeviceId(clientPlayerData, nextDeviceId); // 使用新函数替换直接修改
                    SendAllUserInfoToAllUser();
                } else {
                    if (package.Index < serviceIdList.Count) {
                        using var asymmetricCryptoHelper = new AsymmetricCryptoHelper();
                        var privateKey = Preferences.Get($"key_{serviceIdList[package.Index]}", "");
                        var authorized = Preferences.Get("authentication_" + package.DeviceId, "");
                        if (privateKey != "" && authorized != "") {
                            asymmetricCryptoHelper.ImportPrivateKeyFromPem(privateKey);
                            authenticationId = asymmetricCryptoHelper.Decrypt(authenticationId);
                            if (authorized == authenticationId) {
                                UpdatePlayerDeviceId(clientPlayerData, deviceId); // 使用新函数替换直接修改
                                SendAllUserInfoToAllUser();
                            } else {
                                await SendPublicKey(client, package.Index + 1);
                            }
                        } else {
                            await SendPublicKey(client, package.Index + 1);
                        }
                    } else {
                        await SendPublicKey(client, serviceIdList.Count);
                    }
                }
            }
            return true;
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
                await tcpService.StopAsync();
            }
            if (_tcpClient is TcpClient tcpClient) {
                await tcpClient.CloseAsync();
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
                        SocketReceiveFromResult result = await udpSocket.ReceiveFromAsync(buffer, SocketFlags.None, endpoint, token).ConfigureAwait(false);
                        if (result.ReceivedBytes == 0) {
                            continue;
                        }
                        if (_tcpClient is TcpClient tcpClient) {
                            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                                if (result.RemoteEndPoint is IPEndPoint remoteEndPoint) {
                                    if (myPlayerData.Port != remoteEndPoint.Port) {
                                        BindPortPackage bindPortPackage = new() {
                                            Port = remoteEndPoint.Port,
                                        };
                                        await tcpClient.SendAsync(bindPortPackage, token).ConfigureFalseAwait();
                                        Interlocked.Add(ref myPlayerData.totalSentBytes, bindPortPackage.Length);
                                    }
                                }
                                ForwardPackage forwardPackage = new() {
                                    Port = udpPort,
                                    Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                                };
                                await tcpClient.SendAsync(forwardPackage, token).ConfigureFalseAwait();
                                Interlocked.Add(ref myPlayerData.totalSentBytes, forwardPackage.Length);
                                if (portPlayerDataMap.TryGetValue(udpPort, out PlayerData? targetPlayerData)) {
                                    Interlocked.Add(ref targetPlayerData.totalSentBytes, forwardPackage.Length);
                                }
                            }
                        } else if (_tcpService is TcpService tcpService) {
                            if (result.RemoteEndPoint is IPEndPoint remoteEndPoint && portPlayerDataMap.TryGetValue(udpPort, out PlayerData? targetPlayerData)) {
                                ForwardPackage forwardPackage = new() {
                                    Port = remoteEndPoint.Port,
                                    Bytes = buffer.AsMemory(0, result.ReceivedBytes).ToArray()
                                };
                                await tcpService.SendAsync(targetPlayerData.ClientId, forwardPackage, token).ConfigureFalseAwait();
                                if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                                    Interlocked.Add(ref myPlayerData.totalSentBytes, forwardPackage.Length);
                                }
                                Interlocked.Add(ref targetPlayerData.totalSentBytes, forwardPackage.Length);
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

        private void SendAllUserInfoToAllUser() {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)) {
                AllUserInfoPackage allUserInfoPackage = new() {
                    UserDataList = [.. deviceIdPlayerDataMap.Select(pair => {
                        return new UserData{DeviceId = pair.Value.DeviceId, Port = pair.Value.Port};
                    })]
                };
                if (_tcpService is TcpService tcpService) {
                    // 创建发送任务列表
                    clientIdPlayerDataMap.Where(pair => pair.Key != "").ForEach(pair => Task.Run(async () => {
                        await tcpService.SendAsync(pair.Value.ClientId, allUserInfoPackage);
                        // 原子更新字节计数（在发送后立即执行）
                        Interlocked.Add(ref pair.Value.totalSentBytes, allUserInfoPackage.Length);
                        Interlocked.Add(ref myPlayerData.totalSentBytes, allUserInfoPackage.Length);
                    }));
                }
            }
        }

        private async Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e) {
            PlayerData playerData = new() {
                ClientId = client.Id,
            };
            clientIdPlayerDataMap[client.Id] = playerData;
            MainThread.BeginInvokeOnMainThread(() => {
                playerListViewModel.PlayerDataList.Add(playerData);
            });
            await SendPublicKey(client, 0);
        }

        private async Task SendPublicKey(ITcpSessionClient client, int index) {
            if (clientIdPlayerDataMap.TryGetValue("", out PlayerData? myPlayerData)
                && clientIdPlayerDataMap.TryGetValue(client.Id, out PlayerData? clientPlayerData)) {
                using var asymmetricCryptoHelper = new AsymmetricCryptoHelper();
                lock (keyLock) {
                    if (serviceIdList.Count <= index) {
                        if (serviceIdList.Count > 1000) {
                            throw new Exception("服务ID数量超过限制");
                        }
                        serviceIdList.Add(Guid.NewGuid().ToString());
                        Preferences.Set("serviceIdJson", JsonSerializer.Serialize(serviceIdList));
                    }
                    var privateKey = Preferences.Get($"key_{serviceIdList[index]}", null);
                    if (privateKey is null) {
                        privateKey = asymmetricCryptoHelper.ExportPrivateKeyToPem();
                        Preferences.Set($"key_{serviceIdList[index]}", privateKey);
                    } else {
                        asymmetricCryptoHelper.ImportPrivateKeyFromPem(privateKey);
                    }
                }
                var publicKey = asymmetricCryptoHelper.ExportPublicKeyToPem();
                ServiceIdPackage serviceIdPackage = new() {
                    Index = index,
                    ServiceId = serviceIdList[index],
                    PublicKey = publicKey
                };
                await client.SendAsync(serviceIdPackage);
                Interlocked.Add(ref clientPlayerData.totalSentBytes, serviceIdPackage.Length);
                Interlocked.Add(ref myPlayerData.totalSentBytes, serviceIdPackage.Length);
            }
        }

        private async Task OnClientClosed(ITcpSessionClient client, ClosedEventArgs e) {
            // 移除断开的客户端
            await MainThread.InvokeOnMainThreadAsync(() => {
                if (clientIdPlayerDataMap.TryRemove(client.Id, out var clientPlayerData)) {
                    deviceIdPlayerDataMap.TryRemove(clientPlayerData.DeviceId, out _);
                    portPlayerDataMap.TryRemove(clientPlayerData.Port, out _);
                    playerListViewModel.PlayerDataList.Remove(clientPlayerData);
                    SendAllUserInfoToAllUser();
                }
            }).ConfigureFalseAwait();
        }

        private async Task OnClosed(ITcpClient client, ClosedEventArgs e) {
            await MainThread.InvokeOnMainThreadAsync(() => {
                playerListViewModel.PlayerDataList.Clear();
                deviceIdPlayerDataMap.Clear();
                portPlayerDataMap.Clear();
                serviceId = "";
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
        private static string FormatSpeed(long bytesPerSecond) {
            const long KB = 1024;
            const long MB = KB * 1024;

            return bytesPerSecond switch {
                >= MB => $"{1.0 * bytesPerSecond / MB:F2} MB/s",
                >= KB => $"{1.0 * bytesPerSecond / KB:F2} KB/s",
                _ => $"{bytesPerSecond} B/s"
            };
        }

        private void UpdatePlayerDeviceId(PlayerData playerData, string newDeviceId) {
            // 如果旧DeviceId存在且已在映射中，先移除旧键
            if (!string.IsNullOrEmpty(playerData.DeviceId) && deviceIdPlayerDataMap.ContainsKey(playerData.DeviceId)) {
                deviceIdPlayerDataMap.Remove(playerData.DeviceId, out _);
            }

            // 更新PlayerData的DeviceId属性
            playerData.DeviceId = newDeviceId;

            if (!string.IsNullOrEmpty(playerData.DeviceId)) {
                // 以新DeviceId为键重新添加到映射中
                deviceIdPlayerDataMap[newDeviceId] = playerData;
            }
        }
    }
}
