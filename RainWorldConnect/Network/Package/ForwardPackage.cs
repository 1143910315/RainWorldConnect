using RainWorldConnect.Attributes;
using RainWorldConnect.Network.Base;

namespace RainWorldConnect.Network.Package {
    [GeneratorSerializable]
    public partial class ForwardPackage : BinaryPackageBase {
        [SerializableMember(Index = 0)]
        public int Port { get; set; } = 0;
        [SerializableMember(Index = 1)]
        public byte[] Bytes {
            get; set;
        } = [];
    }
}
