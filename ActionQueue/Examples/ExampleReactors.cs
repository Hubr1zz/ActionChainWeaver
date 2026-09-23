using UnityEngine;

namespace CardGame.ActionQueue.Examples
{
    /// <summary>目标实体自己的伤害修改器；不会影响对其他敌人的伤害。</summary>
    public sealed class FlatDamageReductionReactor : GameActionReactor<DamageAction>
    {
        private readonly int _reduction;

        public FlatDamageReductionReactor(int reduction)
        {
            _reduction = reduction;
        }

        public override ReactionTiming Timing => ReactionTiming.BeforeExecution;
        public override int Priority => 50;

        protected override void React(
            DamageAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            int oldAmount = action.Amount;
            action.SetAmount(oldAmount - _reduction);
            Debug.Log($"[Example] Target reactor reduced damage from {oldAmount} to {action.Amount}.");
        }
    }

    /// <summary>
    /// 注册在某个敌人实体上并指定 Target relation 后，只有攻击该敌人时才会触发。
    /// 判定 Action 会排在 Attack 的 Execute continuation 前面。
    /// </summary>
    public sealed class BossGuardReactor : GameActionReactor<AttackAction>
    {
        private readonly bool _guardCheckSucceeds;

        public BossGuardReactor(bool guardCheckSucceeds)
        {
            _guardCheckSucceeds = guardCheckSucceeds;
        }

        public override ReactionTiming Timing => ReactionTiming.BeforeExecution;
        public override int Priority => 100;

        protected override void React(
            AttackAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            response.EnqueueImmediate(
                new BossGuardCheckAction(action, _guardCheckSucceeds),
                "BossGuardReactor");
        }
    }

    /// <summary>力量判定失败后，目标立即反击来源。</summary>
    public sealed class CounterOnStrengthFailureReactor : GameActionReactor<CheckAction>
    {
        private readonly int _counterDamage;

        public CounterOnStrengthFailureReactor(int counterDamage)
        {
            _counterDamage = counterDamage;
        }

        public override ReactionTiming Timing => ReactionTiming.AfterResolved;

        public override bool Matches(ReactionContext context)
        {
            return context.Outcome.HasValue &&
                   context.Outcome.Value.Status == ActionStatus.Failed &&
                   ((CheckAction)context.Action).Kind == CheckKind.Strength;
        }

        protected override void React(
            CheckAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            response.EnqueueImmediate(
                new DamageAction(
                    (Combatant)action.Target,
                    (Combatant)action.Source,
                    _counterDamage),
                "CounterOnStrengthFailureReactor");
        }
    }

    /// <summary>攻击成功后，攻击者回复生命。</summary>
    public sealed class HealAfterAttackReactor : GameActionReactor<AttackAction>
    {
        private readonly int _amount;

        public HealAfterAttackReactor(int amount)
        {
            _amount = amount;
        }

        public override ReactionTiming Timing => ReactionTiming.AfterResolved;

        public override bool Matches(ReactionContext context)
        {
            return context.Outcome.HasValue && context.Outcome.Value.IsSuccess;
        }

        protected override void React(
            AttackAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            response.EnqueueImmediate(
                new HealAction((Combatant)action.Source, _amount),
                "HealAfterAttackReactor");
        }
    }

    /// <summary>用于当前事件/当前根流程的临时日志 Reactor。</summary>
    public sealed class ChainAttackLoggerReactor : GameActionReactor<AttackAction>
    {
        public override ReactionTiming Timing => ReactionTiming.AfterResolved;
        public override int Priority => -100;

        protected override void React(
            AttackAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            Debug.Log($"[Example] Chain {context.ChainId}: attack resolved as {context.Outcome}.");
        }
    }

    public sealed class DrawOnDamageReactor : GameActionReactor<DamageAction>
    {
        private readonly DeckState _deck;

        public DrawOnDamageReactor(DeckState deck)
        {
            _deck = deck;
        }

        public override ReactionTiming Timing => ReactionTiming.AfterResolved;

        public override bool Matches(ReactionContext context) =>
            context.Outcome.HasValue && context.Outcome.Value.IsSuccess;

        protected override void React(
            DamageAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            response.EnqueueImmediate(new DrawCardAction(_deck, 1), "DrawOnDamageReactor");
        }
    }

    public sealed class DamageOnDrawReactor : GameActionReactor<DrawCardAction>
    {
        private readonly Combatant _source;
        private readonly Combatant _target;

        public DamageOnDrawReactor(Combatant source, Combatant target)
        {
            _source = source;
            _target = target;
        }

        public override ReactionTiming Timing => ReactionTiming.AfterResolved;

        public override bool Matches(ReactionContext context) =>
            context.Outcome.HasValue && context.Outcome.Value.IsSuccess;

        protected override void React(
            DrawCardAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            response.EnqueueImmediate(new DamageAction(_source, _target, 0), "DamageOnDrawReactor");
        }
    }
}
