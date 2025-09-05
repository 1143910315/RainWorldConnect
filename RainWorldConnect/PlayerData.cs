using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace RainWorldConnect {
    public partial class PlayerData : ObservableObject {
        private string _clientId = "";
        public string ClientId {
            get => _clientId;
            set => SetProperty(ref _clientId, value);
        }

        private string _deviceId = "";
        public string DeviceId {
            get => _deviceId;
            set => SetProperty(ref _deviceId, value);
        }

        private string _remark = "";
        public string Remark {
            get => _remark;
            set {
                if (value.IsNullOrWhiteSpace()) {
                    Preferences.Remove("remark_" + DeviceId);
                } else {
                    Preferences.Set("remark_" + DeviceId, value);
                }
                // 设置值并触发通知
                if (SetProperty(ref _remark, value)) {
                    // 当Remark改变时，同时通知DisplayId更新
                    OnPropertyChanged(nameof(DisplayId));
                }
            }
        }

        private int _port = 0;
        public int Port {
            get => _port;
            set => SetProperty(ref _port, value,
                validate: (oldVal, newVal) => newVal >= 0); // 带验证
        }

        public TcpClient? _tcpClient;
        public long lastReceivedBytes = 0;
        public long lastSentBytes = 0;
        public long totalReceivedBytes = 0;
        public long totalSentBytes = 0;
        private string _sentSpeed = "↑0.00B/s";
        public string SentSpeed {
            get => _sentSpeed;
            set => SetProperty(ref _sentSpeed, value);
        }
        private string _revicedSpeed = "↓0.00B/s";
        public string RevicedSpeed {
            get => _revicedSpeed;
            set => SetProperty(ref _revicedSpeed, value);
        }

        // 计算属性
        public string DisplayId => string.IsNullOrWhiteSpace(Remark)
            ? DeviceId
            : Remark;
    }
}
