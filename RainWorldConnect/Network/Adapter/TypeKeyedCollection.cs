using System.Collections.ObjectModel;

namespace RainWorldConnect.Network.Adapter {
    internal partial class TypeKeyedCollection : KeyedCollection<Type, IPackageWrapper> {
        protected override Type GetKeyForItem(IPackageWrapper item) {
            return item.GetPackgeType();
        }
    }
}
