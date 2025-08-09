using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Core;

namespace RainWorldConnect.Network.Data {
    [GeneratorPackage]
    internal partial class UserData : PackageBase {
        [PackageMember(Index = 0)]
        public string DeviceId {
            get; set;
        } = "";
        [PackageMember(Index = 1)]
        public int Port {
            get; set;
        } = 0;
    }
    [GeneratorPackage]
    internal partial class AllUserInfoPackage : PackageBase {
        public const int PackageId = 1;
        [PackageMember(Index = 0)]
        public UserData[] UserDataList {
            get; set;
        } = [];
        public ByteBlock ToByteBlock() {
            ByteBlock byteBlock = new(1024*8);
            byteBlock.WriteInt32(PackageId);
            Package(ref byteBlock);
            return byteBlock;
        }

        public bool FromByteBlock(ByteBlock byteBlock) {
            int packageId = byteBlock.ReadInt32();
            if (packageId == PackageId) {
                Unpackage(ref byteBlock);
                return true;
            }
            byteBlock.SeekToStart();
            return false;
        }
    }
}
