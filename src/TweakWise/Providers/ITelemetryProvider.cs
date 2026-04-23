using System;
using TweakWise.Models;

namespace TweakWise.Providers
{
    public interface ITelemetryProvider
    {
        TimeSpan RefreshInterval { get; }
        TelemetrySnapshot GetSnapshot();
    }
}
