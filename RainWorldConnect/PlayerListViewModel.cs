using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RainWorldConnect {
    partial class PlayerListViewModel : ObservableObject {
        public ObservableCollection<PlayerData> PlayerDataList { get; } = [];
        public ICommand DeleteItemCommand {
            get;
        }

        public PlayerListViewModel() {
            DeleteItemCommand = new Command<PlayerData>(DeleteItem);
        }

        private void DeleteItem(PlayerData item) {
            if (item != null) {
                PlayerDataList.Remove(item);
            }
        }

    }
}
