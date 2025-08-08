using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RainWorldConnect {
    public partial class PlayerData(string deviceId, string remark, int numericId) : ObservableObject {
        private string _deviceId = deviceId;
        public string DeviceId {
            get => _deviceId;
            set => SetProperty(ref _deviceId, value);
        }

        private string _remark = remark;
        public string Remark {
            get => _remark;
            set {
                // 设置值并触发通知
                if (SetProperty(ref _remark, value)) {
                    // 当Remark改变时，同时通知DisplayId更新
                    OnPropertyChanged(nameof(DisplayId));
                }
            }
        }

        private int _numericId = numericId;
        public int NumericId {
            get => _numericId;
            set => SetProperty(ref _numericId, value,
                validate: (oldVal, newVal) => newVal >= 0); // 带验证
        }

        // 计算属性
        public string DisplayId => string.IsNullOrWhiteSpace(Remark)
            ? DeviceId
            : Remark;
    }
}
