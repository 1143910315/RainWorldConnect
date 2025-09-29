using RainWorldConnect.Network.Base;
using TouchSocket.Core;

namespace RainWorldConnect.Network.Adapter {
    internal class PackageWrapper<T>() : IPackageWrapper where T : BinaryPackageBase {
        private readonly T package = Activator.CreateInstance<T>();
        private readonly ParseState state = new();

        public bool Decode<TReader>(ref TReader reader, out BinaryPackageBase request) where TReader : IBytesReader {
            if (state.state < 0) {
                state.state = 0;
                state.strLength = 0;
                state.arrayIndex = 0;
                state.next = null;
            }
            package.Decode(ref reader, state);
            request = package;
            if (state.state == -1) {
                return true;
            } else if (state.state == -2) {
                throw new Exception("Invalid package");
            }
            return false;
        }

        public Type GetPackgeType() {
            return typeof(T);
        }
    }
}
