using TouchSocket.Core;
using TouchSocket.Sockets;

namespace RainWorldConnect.Network.Plugin {
    public partial class TcpClientReceivedPlugin<T, U>(Func<T, U, Task<bool>> callback, U userContext) : PluginBase, ITcpReceivedPlugin {
        public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e) {
            if (e.RequestInfo is not T request || !await callback(request, userContext).ConfigureFalseAwait()) {
                await e.InvokeNext();
            }
        }
    }
}