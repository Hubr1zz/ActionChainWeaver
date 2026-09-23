using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue
{
    public enum ActionExecutionKind
    {
        Command,
        Signal,
        Composite
    }

    [System.Flags]
    public enum ReactionPhases
    {
        None = 0,
        BeforeExecution = 1 << 0,
        AfterResolved = 1 << 1,
        All = BeforeExecution | AfterResolved
    }

    public interface IReactorEntity
    {
        string ReactorName { get; }
    }

    public interface ISourceAction
    {
        IReactorEntity Source { get; }
    }

    public interface ITargetAction
    {
        IReactorEntity Target { get; }
    }

    /// <summary>
    /// 可选的多目标路由能力。普通单目标 Action 继续只实现 Target，避免为常见路径创建集合；
    /// 实现本接口时，Targets 是该 Action 完整的目标集合，Registry 不再读取单值 Target。
    /// </summary>
    public interface IMultiTargetAction
    {
        IReadOnlyList<IReactorEntity> Targets { get; }
    }

    /// <summary>
    /// 一个可观察、可排队的游戏状态变化。Action 不得直接执行其他 Action；
    /// 需要生成后续行为时，使用 ActionExecutionContext 的入队方法。
    /// </summary>
    public abstract class GameAction
    {
        private readonly List<IGameActionReactor> _localReactors = new();
        private readonly List<ScopedReactorBinding> _scopedReactors = new();
        private ActionOutcome? _prevention;
        private bool _wasScheduled;

        public virtual string DebugName => GetType().Name;
        public abstract ActionExecutionKind ExecutionKind { get; }
        public virtual ReactionPhases OpenReactionPhases => ReactionPhases.All;
        internal IReadOnlyList<IGameActionReactor> LocalReactors => _localReactors;
        internal IReadOnlyList<ScopedReactorBinding> ScopedReactors => _scopedReactors;

        internal bool TryMarkScheduled()
        {
            if (_wasScheduled)
                return false;

            _wasScheduled = true;
            return true;
        }

        /// <summary>只监听这个 Action 实例的临时 Reactor。</summary>
        public GameAction AddLocalReactor(IGameActionReactor reactor)
        {
            if (reactor != null)
                _localReactors.Add(reactor);
            return this;
        }

        /// <summary>
        /// 监听这个 Action 及其所有因果后代。作用域随 ActionRuntime 继承，
        /// 不注册进全局 Registry，因此 Chain 结束、取消或失败时无需手工注销。
        /// </summary>
        public GameAction AddSubtreeReactor(IGameActionReactor reactor)
        {
            if (reactor != null)
                _scopedReactors.Add(new ScopedReactorBinding(reactor, true));
            return this;
        }

        /// <summary>只监听这个 Action 的因果后代，不监听 Action 自身。</summary>
        public GameAction AddDescendantReactor(IGameActionReactor reactor)
        {
            if (reactor != null)
                _scopedReactors.Add(new ScopedReactorBinding(reactor, false));
            return this;
        }

        /// <summary>
        /// 供前置检查 Action 使用。它只记录状态；队列会在真正执行本 Action 前统一结算。
        /// </summary>
        public void Prevent(string reason)
        {
            _prevention = ActionOutcome.Prevented(reason);
        }

        internal bool TryGetPrevention(out ActionOutcome outcome)
        {
            if (_prevention.HasValue)
            {
                outcome = _prevention.Value;
                return true;
            }

            outcome = default;
            return false;
        }

        internal UniTask<ActionOutcome> ExecuteInternalAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(context, cancellationToken);
        }

        protected abstract UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>执行权威游戏逻辑的 Action；默认开放执行前与结算后两个响应阶段。</summary>
    public abstract class CommandAction : GameAction
    {
        public sealed override ActionExecutionKind ExecutionKind => ActionExecutionKind.Command;
    }

    /// <summary>
    /// 只发布一个已经发生的游戏逻辑事实，不修改权威状态。
    /// Signal 默认只开放 AfterResolved，事实不能再被 Before Reactor 阻止。
    /// </summary>
    public abstract class SignalAction : GameAction
    {
        public sealed override ActionExecutionKind ExecutionKind => ActionExecutionKind.Signal;
        public sealed override ReactionPhases OpenReactionPhases => ReactionPhases.AfterResolved;

        protected sealed override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            return UniTask.FromResult(ActionOutcome.Success());
        }
    }

    internal readonly struct ScopedReactorBinding
    {
        public ScopedReactorBinding(IGameActionReactor reactor, bool includeOwner)
        {
            Reactor = reactor;
            IncludeOwner = includeOwner;
        }

        public IGameActionReactor Reactor { get; }
        public bool IncludeOwner { get; }
    }

    public sealed class ActionExecutionContext
    {
        private readonly IActionQueueScheduler _scheduler;

        internal ActionExecutionContext(
            IActionQueueScheduler scheduler,
            long chainId,
            long actionId,
            bool skipPresentationWaits)
        {
            _scheduler = scheduler;
            ChainId = chainId;
            ActionId = actionId;
            SkipPresentationWaits = skipPresentationWaits;
        }

        public long ChainId { get; }
        public long ActionId { get; }
        public bool SkipPresentationWaits { get; }

        /// <summary>
        /// 由 Action 明确决定是否等待某次表现；队列的 Debug 配置可以统一跳过这类等待。
        /// 传入的任务仍属于表现系统，ActionQueue 不负责创建、取消或解释表现请求。
        /// </summary>
        public UniTask AwaitPresentationAsync(UniTask completion)
        {
            return SkipPresentationWaits ? UniTask.CompletedTask : completion;
        }

        /// <summary>在当前 Action 完成和 After Reactor 结算后尽快执行。</summary>
        public void EnqueueImmediate(GameAction action, string cause = null)
        {
            _scheduler.EnqueueFromCurrentAction(action, true, ActionId, cause);
        }

        /// <summary>追加到当前根流程的末尾。</summary>
        public void EnqueueToBottom(GameAction action, string cause = null)
        {
            _scheduler.EnqueueFromCurrentAction(action, false, ActionId, cause);
        }
    }
}
