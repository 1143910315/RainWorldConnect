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
    }
}
