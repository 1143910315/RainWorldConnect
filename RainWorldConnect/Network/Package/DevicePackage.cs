using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Core;

namespace RainWorldConnect.Network.Data {
    [GeneratorPackage]
    internal partial class DevicePackage : PackageBase {
        public const int PackageId = 0;
        [PackageMember(Index = 0)]
        public string DeviceId {
            get; set;
        } = "";
        public ByteBlock ToByteBlock() {
            ByteBlock byteBlock = new(64);
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
