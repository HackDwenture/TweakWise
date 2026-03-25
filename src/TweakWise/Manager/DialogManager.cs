using System.Windows;
using Application = System.Windows.Application;

namespace TweakWise.Managers
{
    public class DialogManager
    {
        public AppDialogResult Show(Window owner, string title, string header, string message, AppDialogKind kind = AppDialogKind.Info, AppDialogButtons buttons = AppDialogButtons.Ok)
        {
            var dialog = new AppDialogWindow(title, header, message, kind, buttons);

            if (owner != null && owner.IsLoaded)
                dialog.Owner = owner;
            else if (Application.Current?.MainWindow != null && Application.Current.MainWindow != dialog)
                dialog.Owner = Application.Current.MainWindow;

            dialog.ShowDialog();
            return dialog.Result;
        }

        public AppDialogResult ShowForCurrentWindow(string title, string header, string message, AppDialogKind kind = AppDialogKind.Info, AppDialogButtons buttons = AppDialogButtons.Ok)
        {
            return Show(Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0] as Window : Application.Current?.MainWindow,
                title, header, message, kind, buttons);
        }
    }
}
