using TouchSocket.Core;

namespace RainWorldConnect.Network.Base {
    internal abstract class ISerializeBase {
        public abstract void Encode<TWriter>(ref TWriter writer) where TWriter : IBytesWriter;
        public abstract void Decode<TReader>(ref TReader reader, ParseState parseState) where TReader : IBytesReader;
    }
}