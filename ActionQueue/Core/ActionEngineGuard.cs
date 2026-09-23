using System;
using System.Collections.Generic;

namespace CardGame.ActionQueue
{
    /// <summary>
    /// 队列基础设施完整性检查。与 Reactor/ReactionGate 不同，它不承载游戏玩法、
    /// 不生成 Action、不可被 Buff 屏蔽，并在 Action 进入队列前执行。
    /// </summary>
    public interface IActionEngineGuard
    {
        string Name { get; }
        void ValidateBeforeSchedule(GameAction action);
    }

    public sealed class ActionEngineGuardSet
    {
        private readonly List<IActionEngineGuard> _guards = new();

        public void Add(IActionEngineGuard guard)
        {
            _guards.Add(guard ?? throw new ArgumentNullException(nameof(guard)));
        }

        internal void ValidateBeforeSchedule(GameAction action)
        {
            foreach (IActionEngineGuard guard in _guards)
                guard.ValidateBeforeSchedule(action);
        }
    }
}
