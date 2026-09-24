using System;
using Cysharp.Threading.Tasks;

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
            if (Logger == null || LogLevel < ActionQueueLogLevel.Verbose)
                return;

            try
            {
                Logger.LogVerbose(message);
            }
            catch (Exception exception)
            {
                ReportLoggerException(exception);
            }
        }

        private void LogWarning(string message)
        {
            if (Logger == null || LogLevel < ActionQueueLogLevel.WarningsAndErrors)
                return;

            try
            {
                Logger.LogWarning(message);
            }
            catch (Exception exception)
            {
                ReportLoggerException(exception);
            }
        }

        private void LogException(Exception exception)
        {
            if (Logger == null || LogLevel < ActionQueueLogLevel.WarningsAndErrors)
                return;

            try
            {
                Logger.LogException(exception);
            }
            catch (Exception loggerException)
            {
                ReportLoggerException(loggerException);
            }
        }

        private static void ReportLoggerException(Exception exception)
        {
            try
            {
                // 诊断回调失败不能阻止队列请求完成。
                UniTask.FromException(exception).Forget();
            }
            catch (Exception)
            {
                // 诊断回调失败不能阻止队列请求完成。
            }
        }

        #endregion
    }
}
