using RainWorldConnect.Network.Base;
using System.Buffers.Binary;
using TouchSocket.Core;

namespace RainWorldConnect.Network.Adapter {
    public partial class PackageHandlingAdapter : CustomDataHandlingAdapter<BinaryPackageBase> {
        // 存储不同类型包的回调委托
        private readonly TypeKeyedCollection packageWrapperCollection = [];
        protected override FilterResult Filter<TReader>(ref TReader reader, bool beCached, ref BinaryPackageBase request) {
            IPackageWrapper wrapper;
            var before = reader.BytesRemaining;
            bool frist;
            if (request == null) {
                if (reader.BytesRemaining < 4) {
                    return FilterResult.Cache;
                }
                int packageId = BinaryPrimitives.ReadInt32LittleEndian(reader.GetSpan(4));
                reader.Advance(4);
                wrapper = packageWrapperCollection[packageId];
                frist = true;
            } else {
                wrapper = packageWrapperCollection[request.GetType()];
                frist = false;
            }
            var finish = wrapper.Decode(ref reader, out request);
            if (frist) {
                request.Length = (int)(before - reader.BytesRemaining);
            } else {
                request.Length += (int)(before - reader.BytesRemaining);
            }
            return finish ? FilterResult.Success : FilterResult.Cache;
        }
        public override bool CanSendRequestInfo => true;
        public override void SendInput<TWriter>(ref TWriter writer, IRequestInfo requestInfo) {
            if (requestInfo is BinaryPackageBase package) {
                var before = writer.WrittenCount;
                for (var i = 0; i < packageWrapperCollection.Count; i++) {
                    if (packageWrapperCollection[i].GetPackgeType() == package.GetType()) {
                        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), i);
                        writer.Advance(4);
                        package.Encode(ref writer);
                        package.Length = (int)(writer.WrittenCount - before);
                        return;
                    }
                }
            }
            throw new ArgumentException("Invalid requestInfo type, must be BinaryPackageBase");
        }

        /// <summary>
        /// 注册特定类型包的处理回调
        /// </summary>
        /// <typeparam name="T">包类型，必须继承自BinaryPackageBase并实现IRequestInfo</typeparam>
        public PackageHandlingAdapter Register<T>() where T : BinaryPackageBase {
            var wrapper = new PackageWrapper<T>();
            packageWrapperCollection.Add(wrapper);
            return this;
        }
    }
}