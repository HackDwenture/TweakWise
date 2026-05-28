using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TweakWise.Models;

namespace TweakWise.Managers
{
    public static class HealthSignalActionHelper
    {
        public static async Task<bool> PromptAndApplyAsync(
            Window owner,
            IEnumerable<string> signalIds,
            string targetTitle,
            bool hasProblem)
        {
            var ids = signalIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (ids.Count == 0 || App.ComputerHealthService == null)
                return false;

            string signalWord = ids.Count == 1 ? "сигнал" : "сигналы";
            string header = hasProblem
                ? "Скрыть проблему"
                : "Скрыть рекомендацию";
            string message =
                $"Будут скрыты текущие {signalWord} для блока «{targetTitle}». " +
                "Можно отложить их на неделю или больше не показывать, пока правило подавления не будет сброшено.";

            var result = App.DialogManager.Show(
                owner,
                "Обработка сигнала",
                header,
                message,
                hasProblem ? AppDialogKind.Warning : AppDialogKind.Info,
                AppDialogButtons.PostponeDismissCancel);

            if (result == AppDialogResult.Primary)
            {
                App.ComputerHealthService.SnoozeFindings(ids, TimeSpan.FromDays(7));
            }
            else if (result == AppDialogResult.Secondary)
            {
                App.ComputerHealthService.DismissFindings(ids);
            }
            else
            {
                return false;
            }

            await App.ComputerHealthService.RefreshStatusAsync();
            return true;
        }

        public static bool HasProblem(IReadOnlyList<ModuleHealthFinding> findings)
        {
            return findings?.Any(finding =>
                finding.Level == HealthLevel.Attention ||
                finding.Level == HealthLevel.Warning ||
                finding.Level == HealthLevel.Critical) == true;
        }
    }
}
