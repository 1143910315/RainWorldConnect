using RainWorldConnect.Attributes;
using RainWorldConnect.Network.Base;

namespace RainWorldConnect.Network.Package {
    [GeneratorSerializable]
    internal partial class SwitchServiceIdPackage : BinaryPackageBase {
        [SerializableMember(Index = 0, SkipNullCheck = true)]
        public int Index { get; set; } = 0;
    }
}
