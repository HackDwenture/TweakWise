using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TweakWise.Models;

namespace TweakWise.Services
{
    public interface IComputerHealthService
    {
        event EventHandler HealthStatusChanged;

        ComputerHealthStatus GetOverallStatus();
        IReadOnlyList<CoreModuleDefinition> GetModules();
        CoreModuleDefinition GetModule(CoreModuleId moduleId);
        bool HasFreshStatus(IEnumerable<CoreModuleId> modulesToScan, TimeSpan maxAge);
        void SnoozeFindings(IEnumerable<string> findingIds, TimeSpan duration);
        void DismissFindings(IEnumerable<string> findingIds);
        Task EnsureStatusAsync(IEnumerable<CoreModuleId> modulesToScan, TimeSpan maxAge);
        Task RefreshStatusAsync();
        Task RefreshStatusAsync(IEnumerable<CoreModuleId> modulesToScan);
    }
}
