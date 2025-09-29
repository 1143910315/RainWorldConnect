using RainWorldConnect.Network.Adapter;
using RainWorldConnect.Network.Base;
using RainWorldConnect.Network.Package;
using RainWorldConnect.Network.Plugin;
using TouchSocket.Core;
using TouchSocket.Sockets;
using Windows.ApplicationModel;

namespace RainWorldConnectTests.Socket {
    public class TouchClient {
        TcpClient? _tcpClient;
        public async Task CreateClientAsync() {
            for (var i = 0; i < 50; i++) {
                _tcpClient = new();
                _tcpClient.Closed += async (_, _) => { await Task.Delay(100).ConfigureFalseAwait(); };
                //载入配置
                await _tcpClient.SetupAsync(new TouchSocketConfig()
                     .SetRemoteIPHost("127.0.0.1:12345")
                     .SetTcpDataHandlingAdapter(() => new PackageHandlingAdapter()
                        .Register<AllUserInfoPackage>()
                        .Register<AuthenticationIdPackage>()
                        .Register<BindPortPackage>()
                        .Register<ConfirmRegisterPackage>()
                        .Register<DeviceIdPackage>()
                        .Register<ForwardPackage>()
                        .Register<ServiceIdPackage>()
                        .Register<SwitchServiceIdPackage>()
                     )
                     .ConfigurePlugins(a => {
                         a.UseReconnection<TcpClient>();// 自动重连
                         a.Add(new TcpClientReceivedPlugin<ServiceIdPackage, TcpClient>(async (_, client) => {
                             AuthenticationIdPackage authenticationIdPackage = new() {
                                 Index = 0,
                                 DeviceId = "",
                                 AuthenticationId = ""
                             };
                             await client.SendAsync(authenticationIdPackage);
                             return true;
                         }, _tcpClient));
                     })
                     .ConfigureContainer(a => {
                         a.AddEasyLogger((logLevel, obj, loggerString, ex) => { });//添加一个日志注入
                     })
                ).ConfigureFalseAwait();
                await _tcpClient.ConnectAsync().ConfigureFalseAwait();
                Thread.Sleep(1000);
                await _tcpClient.CloseAsync().ConfigureFalseAwait();
                _tcpClient.Dispose();
            }
        }
    }
}
