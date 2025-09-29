using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RainWorldConnect {
    public partial class ObservableObject : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;

        // 基本通知方法
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 带依赖属性的通知
        protected void OnPropertyChanged(params string[] propertyNames) {
            foreach (var name in propertyNames) {
                OnPropertyChanged(name);
            }
        }

        // 属性设置器（核心）
        protected bool SetProperty<T>(ref T storage, T value,
            [CallerMemberName] string? propertyName = null,
            Action? onChanged = null) {
            if (EqualityComparer<T>.Default.Equals(storage, value)) {
                return false;
            }

            storage = value;
            onChanged?.Invoke();
            OnPropertyChanged(propertyName);
            return true;
        }

        // 带验证的属性设置器
        protected bool SetProperty<T>(ref T storage, T value,
            Func<T, T, bool> validate,
            [CallerMemberName] string? propertyName = null) {
            if (EqualityComparer<T>.Default.Equals(storage, value)) {
                return false;
            }

            if (validate != null && !validate(storage, value)) {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
