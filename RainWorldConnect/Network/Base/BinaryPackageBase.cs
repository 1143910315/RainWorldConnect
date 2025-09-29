using TouchSocket.Core;

namespace RainWorldConnect.Network.Base {
    public abstract class BinaryPackageBase : ISerializeBase, IRequestInfo {
        public int Length {
            get; set;
        }
    }
}
