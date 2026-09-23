using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CardGame.ActionQueue
{
    /// <summary>把 Engine 日志映射到 Unity Console；策略判断仍由 Engine 负责。</summary>
    internal sealed class UnityActionQueueLogger : IActionQueueLogger
    {
        private readonly Object _context;

        public UnityActionQueueLogger(Object context)
        {
            _context = context;
        }

        public void LogVerbose(string message)
        {
            if (_context != null)
                Debug.Log(message, _context);
            else
                Debug.Log(message);
        }

        public void LogWarning(string message)
        {
            if (_context != null)
                Debug.LogWarning(message, _context);
            else
                Debug.LogWarning(message);
        }

        public void LogException(Exception exception)
        {
            if (_context != null)
                Debug.LogException(exception, _context);
            else
                Debug.LogException(exception);
        }
    }
}
