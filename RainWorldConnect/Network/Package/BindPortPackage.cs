using RainWorldConnect.Attributes;
using RainWorldConnect.Network.Base;

namespace RainWorldConnect.Network.Package {
    [GeneratorSerializable]
    public partial class BindPortPackage : BinaryPackageBase {
        [SerializableMember(Index = 0, SkipNullCheck = true)]
        public int Port { get; set; } = 0;
    }
}
