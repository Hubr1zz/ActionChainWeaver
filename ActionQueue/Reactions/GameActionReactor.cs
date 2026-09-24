using System;
using System.Collections.Generic;

namespace CardGame.ActionQueue
{
    public enum ReactionTiming
    {
        BeforeExecution,
        AfterResolved
    }

    [Flags]
    public enum ReactorRelation
    {
        None = 0,
        Source = 1 << 0,
        Target = 1 << 1,
        Either = Source | Target,
        Any = 1 << 2
    }

    public enum ReactionQueuePosition
    {
        Immediate,
        Bottom
    }

    public interface IGameActionReactor
    {
        string Key { get; }
        IReadOnlyCollection<string> Tags { get; }
        string DebugName { get; }
        Type ObservedActionType { get; }
        ReactionTiming Timing { get; }
        int Priority { get; }

        bool Matches(ReactionContext context);
        void React(ReactionContext context, ReactionResponse response);
    }

    public abstract class GameActionReactor<TAction> : IGameActionReactor
        where TAction : GameAction
    {
        private static readonly IReadOnlyCollection<string> EmptyTags = Array.Empty<string>();

        public virtual string Key => GetType().FullName ?? GetType().Name;
        public virtual IReadOnlyCollection<string> Tags => EmptyTags;
        public virtual string DebugName => GetType().Name;
        public Type ObservedActionType => typeof(TAction);
        public abstract ReactionTiming Timing { get; }
        public virtual int Priority => 0;

        public virtual bool Matches(ReactionContext context) => true;

        public void React(ReactionContext context, ReactionResponse response)
        {
            React((TAction)context.Action, context, response);
        }

        protected abstract void React(
            TAction action,
            ReactionContext context,
            ReactionResponse response);
    }

    public sealed class ReactionContext
    {
        internal ReactionContext(
            GameAction action,
            long chainId,
            long actionId,
            ReactorRelation relation,
            IReactorEntity matchedEntity,
            int targetIndex,
            ActionOutcome? outcome)
        {
            Action = action;
            ChainId = chainId;
            ActionId = actionId;
            Relation = relation;
            MatchedEntity = matchedEntity;
            TargetIndex = targetIndex;
            Outcome = outcome;
        }

        public GameAction Action { get; }
        public long ChainId { get; }
        public long ActionId { get; }
        public ReactorRelation Relation { get; }
        /// <summary>通过实体路由匹配到的实体；Global/Chain/Local Reactor 为 null。</summary>
        public IReactorEntity MatchedEntity { get; }
        /// <summary>多目标列表中的下标；非目标实体路由为 -1。</summary>
        public int TargetIndex { get; }
        public ActionOutcome? Outcome { get; }
    }

    public sealed class ReactionResponse
    {
        private readonly List<ReactionActionRequest> _actions = new();

        internal IReadOnlyList<ReactionActionRequest> Actions => _actions;
        internal ActionOutcome? Prevention { get; private set; }
        internal bool StopPropagation { get; private set; }

        public void Prevent(string reason, bool stopPropagation = false)
        {
            Prevention = ActionOutcome.Prevented(reason);
            StopPropagation = stopPropagation;
        }

        public void EnqueueImmediate(GameAction action, string cause = null)
        {
            Add(action, ReactionQueuePosition.Immediate, cause);
        }

        public void EnqueueToBottom(GameAction action, string cause = null)
        {
            Add(action, ReactionQueuePosition.Bottom, cause);
        }

        public void StopFurtherReactors()
        {
            StopPropagation = true;
        }

        private void Add(GameAction action, ReactionQueuePosition position, string cause)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            _actions.Add(new ReactionActionRequest(action, position, cause));
        }
    }

    internal readonly struct ReactionActionRequest
    {
        public ReactionActionRequest(GameAction action, ReactionQueuePosition position, string cause)
        {
            Action = action;
            Position = position;
            Cause = cause ?? string.Empty;
        }

        public GameAction Action { get; }
        public ReactionQueuePosition Position { get; }
        public string Cause { get; }
    }
}
