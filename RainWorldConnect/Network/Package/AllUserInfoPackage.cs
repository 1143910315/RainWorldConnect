using RainWorldConnect.Attributes;
using RainWorldConnect.Network.Base;

namespace RainWorldConnect.Network.Package {
    [GeneratorSerializable]
    public partial class UserData : ISerializeBase {
        [SerializableMember(Index = 0, SkipNullCheck = true)]
        public string DeviceId { get; set; } = "";

        [SerializableMember(Index = 1, SkipNullCheck = true)]
        public int Port { get; set; } = 0;
    }
    [GeneratorSerializable]
    public partial class AllUserInfoPackage : BinaryPackageBase {
        [SerializableMember(Index = 0, SkipNullCheck = true)]
        public UserData[] UserDataList { get; set; } = [];
    }
}
