using System;

namespace CardGame.ActionQueue
{
    /// <summary>由宿主提供的日志出口；纯 Engine 不依赖任何具体日志框架。</summary>
    public interface IActionQueueLogger
    {
        void LogVerbose(string message);
        void LogWarning(string message);
        void LogException(Exception exception);
    }
}
