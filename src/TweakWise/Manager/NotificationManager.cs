using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TweakWise.Models;
using Application = System.Windows.Application;

namespace TweakWise.Managers
{
    public class NotificationManager : INotifyPropertyChanged
    {
        private readonly ObservableCollection<Notification> _notifications = new ObservableCollection<Notification>();
        private readonly SettingsManager _settingsManager;
        private int _unreadCount;

        public NotificationManager(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _notifications.CollectionChanged += (s, e) => UpdateUnreadCount();
            LoadFromSettings();
        }

        public ObservableCollection<Notification> Notifications => _notifications;

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
        public event PropertyChangedEventHandler PropertyChanged;

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
            {
                return () =>
                {
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.NavigateToPage("Explorer");
                };
            }

            if (data.Title == "Автообновление")
            {
                return () =>
                {
                    App.DialogManager?.Show(
                        Application.Current.MainWindow,
                        "Обновление",
                        "Автообновление",
                        "Загрузка обновления будет доступна после подготовки отдельного установщика.",
                        AppDialogKind.Info);
                };
            }

            if (data.Title == "Доступно обновление")
                return () => App.UpdateManager?.OpenLatestReleasePage();

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
            foreach (var notification in _notifications)
            {
                if (!notification.IsRead)
                    notification.IsRead = true;
            }

            UpdateUnreadCount();
            SaveToSettings();
        }

        private void SaveToSettings()
        {
            _settingsManager.CurrentSettings.Notifications = _notifications.Select(notification => new NotificationData
            {
                Title = notification.Title,
                Message = notification.Message,
                IsRead = notification.IsRead,
                HasAction = notification.Action != null
            }).ToList();

            _settingsManager.SaveSettings();
        }

        private void UpdateUnreadCount()
        {
            UnreadCount = _notifications.Count(notification => !notification.IsRead);
            UnreadCountChanged?.Invoke();
        }

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

        public event PropertyChangedEventHandler PropertyChanged;

        private void Execute()
        {
            if (!IsRead)
                IsRead = true;

            Action?.Invoke();
        }

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

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();
    }
}
