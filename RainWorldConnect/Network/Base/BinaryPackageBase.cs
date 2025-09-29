using TouchSocket.Core;

namespace RainWorldConnect.Network.Base {
    internal abstract class BinaryPackageBase : ISerializeBase, IRequestInfo {
        public int Length {
            get; set;
        }
    }
}
