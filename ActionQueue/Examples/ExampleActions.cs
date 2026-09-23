using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CardGame.ActionQueue.Examples
{
    public enum DamageKind
    {
        Attack,
        Spell,
        Reflect,
        DamageOverTime,
        Environment
    }

    [Flags]
    public enum DamageTags
    {
        None = 0,
        Primary = 1 << 0,
        Secondary = 1 << 1,
        IgnoreShieldEligible = 1 << 2,
        CannotCritical = 1 << 3
    }

    public sealed class ChooseTargetAction : CommandAction, ISourceAction
    {
        private readonly Combatant _actor;
        private readonly IReadOnlyList<Combatant> _candidates;
        private readonly Func<IReadOnlyList<Combatant>, CancellationToken, UniTask<Combatant>> _selector;

        public ChooseTargetAction(
            Combatant actor,
            IReadOnlyList<Combatant> candidates,
            Func<IReadOnlyList<Combatant>, CancellationToken, UniTask<Combatant>> selector)
        {
            _actor = actor;
            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        public Combatant SelectedTarget { get; private set; }
        public IReactorEntity Source => _actor;

        protected override async UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            // 输入层负责过滤无效点击；这里仅区分合法选择和玩家取消。
            SelectedTarget = await _selector(_candidates, cancellationToken);
            if (SelectedTarget == null)
                return ActionOutcome.Cancelled("Player cancelled target selection.");

            Debug.Log($"[Example] {_actor.Name} selected {SelectedTarget.Name}.");
            return ActionOutcome.Success();
        }
    }

    public enum CheckKind
    {
        Strength,
        BossGuard
    }

    public sealed class CheckAction : CommandAction, ISourceAction, ITargetAction
    {
        private readonly Combatant _source;
        private readonly Combatant _target;
        private readonly bool _willSucceed;

        public CheckAction(
            Combatant source,
            Combatant target,
            CheckKind kind,
            bool willSucceed)
        {
            _source = source;
            _target = target;
            Kind = kind;
            _willSucceed = willSucceed;
        }

        public CheckKind Kind { get; }
        public IReactorEntity Source => _source;
        public IReactorEntity Target => _target;

        protected override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            Debug.Log($"[Example] {Kind} check: {(_willSucceed ? "success" : "failure")}.");
            return UniTask.FromResult(_willSucceed
                ? ActionOutcome.Success(Kind.ToString())
                : ActionOutcome.Failure(Kind.ToString()));
        }
    }

    [ActionDisplay("Combat/Damage", "造成伤害")]
    public sealed class DamageAction : CommandAction, ISourceAction, ITargetAction
    {
        private readonly Combatant _source;
        private readonly Combatant _target;

        public DamageAction(
            Combatant source,
            Combatant target,
            int amount,
            DamageKind kind = DamageKind.Attack,
            DamageTags tags = DamageTags.Primary,
            long originActionId = 0)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            Amount = amount;
            Kind = kind;
            Tags = tags;
            OriginActionId = originActionId;
        }

        public int Amount { get; private set; }
        public DamageKind Kind { get; }
        public DamageTags Tags { get; }
        public long OriginActionId { get; }
        public IReactorEntity Source => _source;
        public IReactorEntity Target => _target;

        /// <summary>BeforeExecution Reactor 可通过这个受控入口修改最终伤害。</summary>
        public void SetAmount(int amount)
        {
            Amount = Math.Max(0, amount);
        }

        protected override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            _target.TakeDamage(Amount);
            Debug.Log($"[Example] {_source.Name} dealt {Amount} damage to {_target.Name}; {_target}.");
            return UniTask.FromResult(ActionOutcome.Success());
        }
    }

    [ActionDisplay("Combat/Recovery", "恢复生命")]
    public sealed class HealAction : CommandAction, ISourceAction, ITargetAction
    {
        private readonly Combatant _target;
        private readonly int _amount;

        public HealAction(Combatant target, int amount)
        {
            _target = target;
            _amount = amount;
        }

        public IReactorEntity Source => _target;
        public IReactorEntity Target => _target;

        protected override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            _target.Heal(_amount);
            Debug.Log($"[Example] {_target.Name} healed {_amount}; {_target}.");
            return UniTask.FromResult(ActionOutcome.Success());
        }
    }

    public sealed class DrawCardAction : CommandAction, ISourceAction
    {
        private readonly DeckState _deck;
        private readonly int _amount;

        public DrawCardAction(DeckState deck, int amount)
        {
            _deck = deck;
            _amount = amount;
        }

        public DeckState Deck => _deck;
        public IReactorEntity Source => _deck;

        protected override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            _deck.Draw(_amount);
            Debug.Log($"[Example] Drew {_amount} card(s); hand={_deck.CardsInHand}.");
            return UniTask.FromResult(ActionOutcome.Success());
        }
    }

    /// <summary>Boss 前置 Reactor 生成的真正判定 Action。</summary>
    public sealed class BossGuardCheckAction : CommandAction, ISourceAction, ITargetAction
    {
        private readonly AttackAction _attack;
        private readonly bool _willSucceed;

        public BossGuardCheckAction(AttackAction attack, bool willSucceed)
        {
            _attack = attack;
            _willSucceed = willSucceed;
        }

        public IReactorEntity Source => _attack.Target;
        public IReactorEntity Target => _attack.Source;

        protected override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            if (_willSucceed)
            {
                Debug.Log("[Example] Boss guard check passed; attack may continue.");
                return UniTask.FromResult(ActionOutcome.Success());
            }

            _attack.Prevent("Boss guard made this attack invalid.");
            Debug.Log("[Example] Boss guard check failed; attack was prevented.");
            return UniTask.FromResult(ActionOutcome.Failure("BossGuard"));
        }
    }

    /// <summary>完整攻击：力量判定 -> 伤害。它本身仍是可监听、可嵌套的 Action。</summary>
    [ActionDisplay("Combat/Flow", "完整攻击流程")]
    public sealed class AttackAction : CompositeGameAction, ISourceAction, ITargetAction
    {
        private readonly Combatant _source;
        private readonly Combatant _target;
        private readonly int _damage;
        private readonly bool _strengthCheckSucceeds;

        public AttackAction(
            Combatant source,
            Combatant target,
            int damage,
            bool strengthCheckSucceeds)
        {
            _source = source;
            _target = target;
            _damage = damage;
            _strengthCheckSucceeds = strengthCheckSucceeds;
        }

        public IReactorEntity Source => _source;
        public IReactorEntity Target => _target;

        protected override GameAction GetNextChild(CompositeExecutionContext context)
        {
            if (context.CompletedCount == 0)
            {
                return new CheckAction(
                    _source,
                    _target,
                    CheckKind.Strength,
                    _strengthCheckSucceeds);
            }

            if (!context.LastOutcome.IsSuccess)
                return null;

            if (context.CompletedCount == 1)
                return new DamageAction(_source, _target, _damage);

            return null;
        }

        protected override ActionOutcome Resolve(CompositeExecutionContext context)
        {
            if (TryGetPrevention(out ActionOutcome prevention))
                return prevention;

            if (context.CompletedCount == 0)
                return ActionOutcome.Failure("Attack had no steps.");

            return context.LastOutcome;
        }
    }

    /// <summary>选择目标 -> 嵌套一个完整 AttackAction，演示“父 Action 也能成为子 Action”。</summary>
    public sealed class AttackFlowAction : CompositeGameAction, ISourceAction
    {
        private readonly Combatant _source;
        private readonly IReadOnlyList<Combatant> _targets;
        private readonly int _damage;
        private readonly bool _strengthCheckSucceeds;
        private ChooseTargetAction _chooseTarget;

        public AttackFlowAction(
            Combatant source,
            IReadOnlyList<Combatant> targets,
            int damage,
            bool strengthCheckSucceeds)
        {
            _source = source;
            _targets = targets;
            _damage = damage;
            _strengthCheckSucceeds = strengthCheckSucceeds;
        }

        public IReactorEntity Source => _source;

        protected override GameAction GetNextChild(CompositeExecutionContext context)
        {
            if (context.CompletedCount == 0)
            {
                _chooseTarget = new ChooseTargetAction(
                    _source,
                    _targets,
                    static (targets, cancellationToken) =>
                        UniTask.FromResult(targets.Count > 0 ? targets[0] : null));
                return _chooseTarget;
            }

            if (!context.LastOutcome.IsSuccess)
                return null;

            if (context.CompletedCount == 1)
            {
                return new AttackAction(
                    _source,
                    _chooseTarget.SelectedTarget,
                    _damage,
                    _strengthCheckSucceeds);
            }

            return null;
        }

        protected override ActionOutcome Resolve(CompositeExecutionContext context)
        {
            return context.CompletedCount == 0
                ? ActionOutcome.Failure("Attack flow had no steps.")
                : context.LastOutcome;
        }
    }
}
