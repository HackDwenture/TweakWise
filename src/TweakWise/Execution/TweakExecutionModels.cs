using System;
using System.Collections.Generic;
using Microsoft.Win32;
using TweakWise.Models;

namespace TweakWise.Execution
{
    public sealed class TweakExecutionOptions
    {
        public bool DryRun { get; set; }
        public bool DangerousChangeConfirmed { get; set; }
    }

    public sealed class TweakExecutionResult
    {
        public bool Success { get; set; }
        public bool Failed => !Success;
        public bool RequiresRestart { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorDetails { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public bool RollbackAvailable { get; set; }

        public static TweakExecutionResult Ok(string message)
        {
            return new TweakExecutionResult
            {
                Success = true,
                Message = message
            };
        }

        public static TweakExecutionResult Fail(string message, string errorDetails = "")
        {
            return new TweakExecutionResult
            {
                Success = false,
                Message = message,
                ErrorDetails = errorDetails
            };
        }
    }

    public sealed class TweakStateReadResult
    {
        public bool Success { get; set; }
        public string CurrentState { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ErrorDetails { get; set; } = string.Empty;

        public static TweakStateReadResult Ok(string currentState)
        {
            return new TweakStateReadResult
            {
                Success = true,
                CurrentState = currentState
            };
        }

        public static TweakStateReadResult Fail(string message, string errorDetails = "")
        {
            return new TweakStateReadResult
            {
                Success = false,
                Message = message,
                ErrorDetails = errorDetails
            };
        }
    }

    public sealed class TweakExecutionHistoryItem
    {
        public string TweakId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; } = DateTime.Now;
        public string Message { get; set; } = string.Empty;
        public bool RequiresRestart { get; set; }
        public bool RollbackAvailable { get; set; }
    }

    public sealed class RegistryRollbackRecord
    {
        public string TweakId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<RegistryRollbackValue> Values { get; set; } = new List<RegistryRollbackValue>();
    }

    public sealed class RegistryRollbackValue
    {
        public RegistryTweakHive Hive { get; set; } = RegistryTweakHive.CurrentUser;
        public string SubKeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public bool KeyExisted { get; set; }
        public bool ValueExisted { get; set; }
        public object OldValue { get; set; }
        public RegistryValueKind OldValueKind { get; set; } = RegistryValueKind.Unknown;
        public bool AllowValueDeleteWhenMissing { get; set; }
    }
}
