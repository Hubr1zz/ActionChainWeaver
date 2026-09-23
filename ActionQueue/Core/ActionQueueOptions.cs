using System;

namespace CardGame.ActionQueue
{
    /// <summary>创建纯 C# ActionQueueEngine 时使用的稳定配置。</summary>
    public sealed class ActionQueueOptions
    {
        private int _maxActionsPerChain = 128;
        private int _traceCapacity = 24;
        private ActionQueueLogLevel _logLevel = ActionQueueLogLevel.WarningsAndErrors;

        public int MaxActionsPerChain
        {
            get => _maxActionsPerChain;
            set => _maxActionsPerChain = value >= 1
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public int TraceCapacity
        {
            get => _traceCapacity;
            set => _traceCapacity = value >= 4
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
        }

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

        /// <summary>
        /// Debug 用：Action 调用 ActionExecutionContext.AwaitPresentationAsync 时立即继续。
        /// 不会取消表现，也不会伪造表现 Handle 的真实完成状态。
        /// </summary>
        public bool SkipPresentationWaits { get; set; }
    }
}
