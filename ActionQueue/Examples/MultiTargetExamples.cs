using System;
using System.Collections.Generic;

namespace CardGame.ActionQueue.Examples
{
    /// <summary>
    /// 多目标攻击意图。BeforeExecution 阶段会先路由所有目标实体的 Reactor；
    /// 只有父 Action 未被阻止时，才逐个展开单目标 DamageAction。
    /// </summary>
    public sealed class BossAoeAttackAction : CompositeGameAction, ISourceAction, IMultiTargetAction
    {
        private readonly Combatant _boss;
        private readonly Combatant[] _damageTargets;
        private readonly IReactorEntity[] _reactorTargets;

        public BossAoeAttackAction(
            Combatant boss,
            IReadOnlyList<Combatant> targets)
        {
            _boss = boss ?? throw new ArgumentNullException(nameof(boss));
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            _damageTargets = new Combatant[targets.Count];
            _reactorTargets = new IReactorEntity[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                Combatant target = targets[i] ??
                    throw new ArgumentException("AOE targets cannot contain null.", nameof(targets));
                _damageTargets[i] = target;
                _reactorTargets[i] = target;
            }
        }

        public IReactorEntity Source => _boss;
        public IReadOnlyList<IReactorEntity> Targets => _reactorTargets;

        protected override GameAction GetNextChild(CompositeExecutionContext context)
        {
            if (context.CompletedCount > 0 && !context.LastOutcome.IsSuccess)
                return null;

            return context.CompletedCount < _damageTargets.Length
                ? new DamageAction(_boss, _damageTargets[context.CompletedCount], 5)
                : null;
        }
    }

    /// <summary>注册在任一受保护目标上，即可在第一段伤害前阻止整个 Boss AOE。</summary>
    public sealed class PreventBossAoeReactor : GameActionReactor<BossAoeAttackAction>
    {
        private readonly Combatant _boss;

        public PreventBossAoeReactor(Combatant boss)
        {
            _boss = boss;
        }

        public override ReactionTiming Timing => ReactionTiming.BeforeExecution;

        public override bool Matches(ReactionContext context)
        {
            return context.Action is ISourceAction sourceAction &&
                   ReferenceEquals(sourceAction.Source, _boss) &&
                   context.MatchedEntity != null &&
                   context.TargetIndex >= 0;
        }

        protected override void React(
            BossAoeAttackAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            response.Prevent(
                $"{context.MatchedEntity.ReactorName} prevented the complete Boss AOE.",
                stopPropagation: true);
        }
    }
}
