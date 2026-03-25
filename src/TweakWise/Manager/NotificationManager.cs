using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TweakWise.Models;

namespace TweakWise.Managers
{
    public class NotificationManager : INotifyPropertyChanged
    {
        private ObservableCollection<Notification> _notifications = new ObservableCollection<Notification>();
        public ObservableCollection<Notification> Notifications => _notifications;

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            private set
            {
                _unreadCount = value;
                OnPropertyChanged(nameof(UnreadCount));
            }
        }

        public event Action UnreadCountChanged;

        private SettingsManager _settingsManager;

        public NotificationManager(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _notifications.CollectionChanged += (s, e) => UpdateUnreadCount();
            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _notifications.Clear();
            foreach (var data in _settingsManager.CurrentSettings.Notifications)
            {
                var notification = new Notification
                {
                    Title = data.Title,
                    Message = data.Message,
                    IsRead = data.IsRead,
                    Action = data.HasAction ? CreateDemoAction(data) : null
                };
                _notifications.Add(notification);
            }
            UpdateUnreadCount();
        }

        private Action CreateDemoAction(NotificationData data)
        {
            if (data.Title == "Новые настройки")
                return () =>
                {
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.NavigateToPage("Explorer");
                };
            if (data.Title == "Автообновление")
                return () => MessageBox.Show("Загрузка обновления...", "Обновление");
            return null;
        }

        public void AddNotification(string title, string message, Action action = null)
        {
            var notification = new Notification
            {
                Title = title,
                Message = message,
                Action = action,
                IsRead = false
            };
            _notifications.Add(notification);
            SaveToSettings();
        }

        public void ClearAll()
        {
            _notifications.Clear();
            SaveToSettings();
        }

        public void MarkAllAsRead()
        {
            foreach (var n in _notifications)
                if (!n.IsRead) n.IsRead = true;
            UpdateUnreadCount();
            SaveToSettings();
        }

        private void SaveToSettings()
        {
            _settingsManager.CurrentSettings.Notifications = _notifications.Select(n => new NotificationData
            {
                Title = n.Title,
                Message = n.Message,
                IsRead = n.IsRead,
                HasAction = n.Action != null
            }).ToList();
            _settingsManager.SaveSettings();
        }

        private void UpdateUnreadCount()
        {
            int count = _notifications.Count(n => !n.IsRead);
            UnreadCount = count;
            UnreadCountChanged?.Invoke();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Notification : INotifyPropertyChanged
    {
        private bool _isRead;
        public string Title { get; set; }
        public string Message { get; set; }
        public Action Action { get; set; }
        public bool IsRead
        {
            get => _isRead;
            set
            {
                _isRead = value;
                OnPropertyChanged(nameof(IsRead));
            }
        }

        public ICommand ClickCommand => new RelayCommand(Execute);

        private void Execute()
        {
            if (!IsRead)
                IsRead = true;
            Action?.Invoke();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}