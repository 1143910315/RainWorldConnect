using RainWorldConnect.Network.Base;
using TouchSocket.Core;

namespace RainWorldConnect.Network.Adapter {
    internal interface IPackageWrapper : IRequestInfo {
        bool Decode<TReader>(ref TReader reader, out BinaryPackageBase request) where TReader : IBytesReader;
        Type GetPackgeType();
    }
}
