using RainWorldConnect.Attributes;
using RainWorldConnect.Network.Base;

namespace RainWorldConnect.Network.Package {
    [GeneratorSerializable]
    public partial class DeviceIdPackage : BinaryPackageBase {
        [SerializableMember(Index = 0, SkipNullCheck = true)]
        public string DeviceId { get; set; } = "";
    }
}
