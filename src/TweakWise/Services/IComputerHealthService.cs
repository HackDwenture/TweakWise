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
        void SnoozeFindings(IEnumerable<string> findingIds, TimeSpan duration);
        void DismissFindings(IEnumerable<string> findingIds);
        Task RefreshStatusAsync();
    }
}
