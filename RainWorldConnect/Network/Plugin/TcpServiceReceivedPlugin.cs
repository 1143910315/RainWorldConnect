using TouchSocket.Core;
using TouchSocket.Sockets;

namespace RainWorldConnect.Network.Plugin {
    internal partial class TcpServiceReceivedPlugin<T, U>(Func<ITcpSessionClient, T, U, Task<bool>> callback, U userContext) : PluginBase, ITcpReceivedPlugin {
        public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e) {
            if (e.RequestInfo is not T request || client is not ITcpSessionClient sessionClient || !await callback(sessionClient, request, userContext).ConfigureFalseAwait()) {
                await e.InvokeNext();
            }
        }
    }
}