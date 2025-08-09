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
        public ICommand AddItemCommand {
            get;
        }
        public ICommand DeleteItemCommand {
            get;
        }

        private static int _counter = 1;
        private static readonly Random _random = new();

        public PlayerListViewModel() {
            // 添加初始示例数据
            for (int i = 0; i < 5; i++) {
                AddNewItem();
            }

            AddItemCommand = new Command(AddNewItem);
            DeleteItemCommand = new Command<PlayerData>(DeleteItem);
        }

        private void AddNewItem() {
            var deviceId = GenerateDeviceId();
            PlayerDataList.Add(new PlayerData {
                DeviceId = deviceId,
                Remark = "",
                Port = _counter++
            });
        }

        private void DeleteItem(PlayerData item) {
            if (item != null) {
                PlayerDataList.Remove(item);
            }
        }

        private string GenerateDeviceId() {
            var bytes = new byte[6];
            _random.NextBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "");
        }
    }
}
