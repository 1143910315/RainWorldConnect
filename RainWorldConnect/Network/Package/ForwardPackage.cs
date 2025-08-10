using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Core;

namespace RainWorldConnect.Network.Data {
    [GeneratorPackage]
    internal partial class ForwardPackage : PackageBase {
        public const int PackageId = 2;
        [PackageMember(Index = 0)]
        public int Port { get; set; } = 0;
        [PackageMember(Index = 1)]
        public byte[] Bytes {
            get; set;
        } = [];
        public ByteBlock ToByteBlock() {
            ByteBlock byteBlock = new(1024 * 8);
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
