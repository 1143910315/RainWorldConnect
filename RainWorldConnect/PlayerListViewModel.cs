using System.Collections.ObjectModel;

namespace RainWorldConnect {
    partial class PlayerListViewModel : ObservableObject {
        public ObservableCollection<PlayerData> PlayerDataList { get; } = [];
    }
}
