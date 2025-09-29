using RainWorldConnect.Attributes;
using RainWorldConnect.Network.Base;

namespace RainWorldConnect.Network.Package {
    [GeneratorSerializable]
    public partial class ServiceIdPackage : BinaryPackageBase {
        [SerializableMember(Index = 0, SkipNullCheck = true)]
        public int Index { get; set; } = 0;
        [SerializableMember(Index = 1, SkipNullCheck = true)]
        public string ServiceId { get; set; } = "";
        [SerializableMember(Index = 2, SkipNullCheck = true)]
        public string PublicKey { get; set; } = "";
    }
}
