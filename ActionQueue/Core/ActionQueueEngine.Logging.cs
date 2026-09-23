using System;

namespace CardGame.ActionQueue
{
    public sealed partial class ActionQueueEngine
    {
        #region Logging

        private IActionQueueLogger Logger { get; }
        private ActionQueueLogLevel _logLevel;

        public ActionQueueLogLevel LogLevel
        {
            get => _logLevel;
            set
            {
                if (value < ActionQueueLogLevel.None || value > ActionQueueLogLevel.Verbose)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _logLevel = value;
            }
        }

        private void LogVerbose(string message)
        {
            if (LogLevel >= ActionQueueLogLevel.Verbose)
                Logger?.LogVerbose(message);
        }

        private void LogWarning(string message)
        {
            if (LogLevel >= ActionQueueLogLevel.WarningsAndErrors)
                Logger?.LogWarning(message);
        }

        private void LogException(Exception exception)
        {
            if (LogLevel >= ActionQueueLogLevel.WarningsAndErrors)
                Logger?.LogException(exception);
        }

        #endregion
    }
}
