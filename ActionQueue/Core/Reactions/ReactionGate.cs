using System;
using System.Collections.Generic;

namespace CardGame.ActionQueue
{
    /// <summary>
    /// 游戏逻辑层的选择性响应准入规则。它不执行响应，只决定某个 Reactor 是否允许观察某个 Action。
    /// 系统完整性检查不放在这里，而由独立的 EngineGuard 负责。
    /// </summary>
    public interface IReactionGate
    {
        string Id { get; }
        int Priority { get; }
        bool Allows(ReactionGateContext context, out string reason);
    }

    public readonly struct ReactionGateContext
    {
        internal ReactionGateContext(
            GameAction action,
            IGameActionReactor reactor,
            ReactionTiming timing,
            ReactorRelation relation,
            IReactorEntity matchedEntity,
            long chainId,
            long actionId,
            ActionOutcome? outcome)
        {
            Action = action;
            Reactor = reactor;
            Timing = timing;
            Relation = relation;
            MatchedEntity = matchedEntity;
            ChainId = chainId;
            ActionId = actionId;
            Outcome = outcome;
        }

        public GameAction Action { get; }
        public IGameActionReactor Reactor { get; }
        public ReactionTiming Timing { get; }
        public ReactorRelation Relation { get; }
        public IReactorEntity MatchedEntity { get; }
        public long ChainId { get; }
        public long ActionId { get; }
        public ActionOutcome? Outcome { get; }
    }

    public sealed class ReactionGateRegistry
    {
        private readonly List<Registration> _gates = new();
        private long _nextOrder;
        private bool _evaluating;

        public IDisposable Register(IReactionGate gate)
        {
            ThrowIfEvaluating();
            if (gate == null)
                throw new ArgumentNullException(nameof(gate));
            var registration = new Registration(this, gate, _nextOrder++);
            _gates.Add(registration);
            _gates.Sort(RegistrationComparer.Instance);
            return registration;
        }

        internal bool Allows(ReactionGateContext context, out string reason)
        {
            if (_evaluating)
                throw new InvalidOperationException("ReactionGateRegistry does not support recursive evaluation.");
            _evaluating = true;
            try
            {
                foreach (Registration registration in _gates)
                {
                    if (!registration.Gate.Allows(context, out reason))
                        return false;
                }
                reason = string.Empty;
                return true;
            }
            finally
            {
                _evaluating = false;
            }
        }

        private void ThrowIfEvaluating()
        {
            if (_evaluating)
                throw new InvalidOperationException("Reaction gates cannot change during evaluation.");
        }

        private sealed class Registration : IDisposable
        {
            private ReactionGateRegistry _owner;
            public Registration(ReactionGateRegistry owner, IReactionGate gate, long order)
            { _owner = owner; Gate = gate; Order = order; }
            public IReactionGate Gate { get; }
            public long Order { get; }
            public void Dispose()
            {
                ReactionGateRegistry owner = _owner;
                if (owner == null) return;
                owner.ThrowIfEvaluating();
                _owner = null;
                owner._gates.Remove(this);
            }
        }

        private sealed class RegistrationComparer : IComparer<Registration>
        {
            public static readonly RegistrationComparer Instance = new();
            public int Compare(Registration x, Registration y)
            {
                int priority = y.Gate.Priority.CompareTo(x.Gate.Priority);
                return priority != 0 ? priority : x.Order.CompareTo(y.Order);
            }
        }
    }
}
