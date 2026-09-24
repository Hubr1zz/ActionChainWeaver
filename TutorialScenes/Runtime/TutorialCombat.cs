using System;
using System.Collections.Generic;
using System.Threading;
using CardGame.ActionQueue;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CardGame.ActionQueue.Tutorial
{
    public sealed class TutorialReactorScene : MonoBehaviour
    {
        [SerializeField] private ActionQueueRunner runner;
        [SerializeField] private TutorialView view;

        private readonly List<IDisposable> registrations = new();
        private TutorialFighter hero;
        private TutorialFighter boss;
        private TutorialFighter slime;

        private void Start()
        {
            hero = new TutorialFighter("Hero", TutorialConsts.HeroMaxHp);
            boss = new TutorialFighter("Boss", TutorialConsts.BossMaxHp);
            slime = new TutorialFighter("Slime", TutorialConsts.SlimeMaxHp);
            hero.TakeDamage(5);
            registrations.Add(runner.Reactors.RegisterGlobal(new TutorialGlobalLogReactor(view.Append)));
            registrations.Add(runner.Reactors.RegisterForEntity(hero, new TutorialHealReactor(view.Append), ReactorRelation.Source));
            registrations.Add(runner.Reactors.RegisterForEntity(boss, new TutorialGuardReactor(view.Append), ReactorRelation.Target));
            registrations.Add(runner.Reactors.RegisterForEntity(boss, new TutorialArmorReactor(TutorialConsts.BossArmor, view.Append), ReactorRelation.Target));
            registrations.Add(runner.Reactors.RegisterForEntity(boss, new TutorialCounterReactor(view.Append), ReactorRelation.Target));

            view.Initialize(new TutorialText("02  /  REACTORS", "02  /  响应器"), new TutorialText("Global rules persist; chain rules live for one queued root. Entity rules route by source/target: Boss has guard, armor and counter; Slime does not. A local rule watches one Attack instance.", "全局规则持续生效；当前链规则只作用于一次根行动。实体规则按来源／目标路由：首领有守卫、护甲和反击，史莱姆没有。局部规则只观察一个攻击实例。"));
            view.SetRules(TutorialRuleText.ReactorsLocalized);
            view.SetButton(0, new TutorialText("ATTACK SLIME", "攻击史莱姆"), () => Run(slime, true, true).Forget());
            view.SetButton(1, new TutorialText("ATTACK BOSS", "攻击首领"), () => Run(boss, true, true).Forget());
            view.SetButton(2, new TutorialText("BOSS PREVENTS", "首领阻止攻击"), () => Run(boss, false, true).Forget());
            view.SetButton(3, new TutorialText("CHECK FAILS", "力量判定失败"), () => Run(boss, true, false).Forget());
            view.SetButton(4, new TutorialText("RESET HP", "重置生命"), ResetState);
            view.HideUnusedButtons(5);
            UpdateState();
        }

        private async UniTask Run(TutorialFighter target, bool guardAllows, bool checkSucceeds)
        {
            if (runner.IsRunning)
                return;

            view.ClearHistory();
            view.Append(new TutorialText($"Hero attacks {target.ReactorName} | guard={guardAllows} | roll={checkSucceeds}", $"玩家攻击{TutorialLocalization.ChineseEntityName(target.ReactorName)}｜守卫通过：{(guardAllows ? "是" : "否")}｜判定成功：{(checkSucceeds ? "是" : "否")}"));
            var attack = new TutorialAttackAction(hero, target, TutorialConsts.AttackDamage, guardAllows, checkSucceeds, view.Append);
            attack.AddLocalReactor(new TutorialLocalLogReactor(view.Append));
            IGameActionReactor[] chainReactors = { new TutorialChainLogReactor(view.Append) };
            ActionOutcome outcome = await runner.Enqueue(attack, chainReactors);
            view.Append(new TutorialText($"ROOT => {outcome}", $"根行动 => {TutorialLocalization.ChineseStatus(outcome.Status)}"));
            UpdateState();
        }

        private void ResetState()
        {
            if (runner.IsRunning)
                return;

            hero.Restore(15);
            boss.Restore(TutorialConsts.BossMaxHp);
            slime.Restore(TutorialConsts.SlimeMaxHp);
            view.ClearHistory();
            UpdateState();
        }

        private void UpdateState()
        {
            view.SetState(new TutorialText($"Hero {hero.Hp}/{hero.MaxHp}    |    Boss {boss.Hp}/{boss.MaxHp}    |    Slime {slime.Hp}/{slime.MaxHp}", $"玩家 {hero.Hp}/{hero.MaxHp}    |    首领 {boss.Hp}/{boss.MaxHp}    |    史莱姆 {slime.Hp}/{slime.MaxHp}"));
        }

        private void OnDestroy()
        {
            foreach (IDisposable registration in registrations)
                registration.Dispose();
        }
    }

    internal sealed class TutorialFighter : IReactorEntity
    {
        public TutorialFighter(string name, int maxHp)
        {
            ReactorName = name;
            MaxHp = maxHp;
            Hp = maxHp;
        }

        public string ReactorName { get; }
        public int MaxHp { get; }
        public int Hp { get; private set; }
        public int Block { get; private set; }

        public void TakeDamage(int amount) => Hp = Math.Max(0, Hp - Math.Max(0, amount));
        public void Heal(int amount) => Hp = Math.Min(MaxHp, Hp + Math.Max(0, amount));
        public void Restore(int hp) => Hp = Math.Max(0, Math.Min(MaxHp, hp));
        public void SetBlock(int amount) => Block = Math.Max(0, amount);
        public void SpendBlock(int amount) => Block = Math.Max(0, Block - Math.Max(0, amount));
    }

    internal sealed class TutorialAttackAction : CompositeGameAction, ISourceAction, ITargetAction
    {
        private readonly TutorialFighter source;
        private readonly TutorialFighter target;
        private readonly int damage;
        private readonly bool checkSucceeds;
        private readonly Action<TutorialText> trace;

        public TutorialAttackAction(TutorialFighter source, TutorialFighter target, int damage, bool guardAllows, bool checkSucceeds, Action<TutorialText> trace)
        {
            this.source = source;
            this.target = target;
            this.damage = damage;
            this.checkSucceeds = checkSucceeds;
            this.trace = trace;
            GuardAllows = guardAllows;
        }

        public bool GuardAllows { get; }
        public IReactorEntity Source => source;
        public IReactorEntity Target => target;
        public override string DebugName => $"Attack {target.ReactorName}";

        protected override GameAction GetNextChild(CompositeExecutionContext context)
        {
            if (context.CompletedCount == 0)
                return new TutorialCheckAction(source, target, checkSucceeds, trace);
            if (!context.LastOutcome.IsSuccess)
                return null;
            if (context.CompletedCount == 1)
                return new TutorialDamageAction(source, target, damage, trace);
            return null;
        }

        protected override ActionOutcome Resolve(CompositeExecutionContext context)
        {
            if (TryGetPrevention(out ActionOutcome prevention))
                return prevention;
            return context.CompletedCount == 0 ? ActionOutcome.Failure("Attack had no steps") : context.LastOutcome;
        }
    }

    internal sealed class TutorialCheckAction : CommandAction, ISourceAction, ITargetAction
    {
        private readonly TutorialFighter source;
        private readonly TutorialFighter target;
        private readonly bool succeeds;
        private readonly Action<TutorialText> trace;

        public TutorialCheckAction(TutorialFighter source, TutorialFighter target, bool succeeds, Action<TutorialText> trace)
        {
            this.source = source;
            this.target = target;
            this.succeeds = succeeds;
            this.trace = trace;
        }

        public IReactorEntity Source => source;
        public IReactorEntity Target => target;
        public override string DebugName => "Strength check";

        protected override UniTask<ActionOutcome> ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            trace(new TutorialText($"Strength check => {(succeeds ? "Succeeded" : "Failed")}", $"力量判定 => {(succeeds ? "成功" : "失败")}"));
            return UniTask.FromResult(succeeds ? ActionOutcome.Success() : ActionOutcome.Failure("Strength check failed"));
        }
    }

    internal sealed class TutorialDamageAction : CommandAction, ISourceAction, ITargetAction
    {
        private readonly TutorialFighter source;
        private readonly TutorialFighter target;
        private readonly Action<TutorialText> trace;
        private int blockToConsume;

        public TutorialDamageAction(TutorialFighter source, TutorialFighter target, int amount, Action<TutorialText> trace)
        {
            this.source = source;
            this.target = target;
            this.trace = trace;
            Amount = amount;
        }

        public int Amount { get; private set; }
        public IReactorEntity Source => source;
        public IReactorEntity Target => target;
        public override string DebugName => $"Damage {target.ReactorName}";
        public void SetAmount(int amount) => Amount = Math.Max(0, amount);
        public void SetBlockConsumption(int amount) => blockToConsume = Math.Max(0, amount);

        protected override UniTask<ActionOutcome> ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            if (blockToConsume > 0)
            {
                target.SpendBlock(blockToConsume);
                trace(new TutorialText($"{target.ReactorName} blocks {blockToConsume}; Block remaining {target.Block}", $"{TutorialLocalization.ChineseEntityName(target.ReactorName)}格挡 {blockToConsume} 点；剩余格挡 {target.Block}"));
            }

            target.TakeDamage(Amount);
            trace(new TutorialText($"{target.ReactorName} takes {Amount} damage => HP {target.Hp}", $"{TutorialLocalization.ChineseEntityName(target.ReactorName)}受到 {Amount} 点伤害 => 生命 {target.Hp}"));
            return UniTask.FromResult(ActionOutcome.Success());
        }
    }

    internal sealed class TutorialHealAction : CommandAction, ISourceAction
    {
        private readonly TutorialFighter target;
        private readonly Action<TutorialText> trace;

        public TutorialHealAction(TutorialFighter target, Action<TutorialText> trace)
        {
            this.target = target;
            this.trace = trace;
        }

        public IReactorEntity Source => target;
        public override string DebugName => "Heal after attack";

        protected override UniTask<ActionOutcome> ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            target.Heal(1);
            trace(new TutorialText($"{target.ReactorName} heals 1 => HP {target.Hp}", $"{TutorialLocalization.ChineseEntityName(target.ReactorName)}恢复 1 点生命 => 生命 {target.Hp}"));
            return UniTask.FromResult(ActionOutcome.Success());
        }
    }

    internal sealed class TutorialGlobalLogReactor : GameActionReactor<GameAction>
    {
        private readonly Action<TutorialText> trace;
        public TutorialGlobalLogReactor(Action<TutorialText> trace) => this.trace = trace;
        public override ReactionTiming Timing => ReactionTiming.AfterResolved;
        public override int Priority => -100;
        protected override void React(GameAction action, ReactionContext context, ReactionResponse response) => trace(new TutorialText($"[Global] {action.DebugName}: {context.Outcome?.Status}", $"[全局] {TutorialLocalization.ChineseActionName(action.DebugName)}：{TutorialLocalization.ChineseStatus(context.Outcome?.Status)}"));
    }

    internal sealed class TutorialLocalLogReactor : GameActionReactor<TutorialAttackAction>
    {
        private readonly Action<TutorialText> trace;
        public TutorialLocalLogReactor(Action<TutorialText> trace) => this.trace = trace;
        public override ReactionTiming Timing => ReactionTiming.AfterResolved;
        protected override void React(TutorialAttackAction action, ReactionContext context, ReactionResponse response) => trace(new TutorialText($"[Local] This attack: {context.Outcome?.Status}", $"[局部] 本次攻击：{TutorialLocalization.ChineseStatus(context.Outcome?.Status)}"));
    }

    internal sealed class TutorialChainLogReactor : GameActionReactor<GameAction>
    {
        private readonly Action<TutorialText> trace;
        public TutorialChainLogReactor(Action<TutorialText> trace) => this.trace = trace;
        public override ReactionTiming Timing => ReactionTiming.AfterResolved;
        public override int Priority => -90;
        protected override void React(GameAction action, ReactionContext context, ReactionResponse response) => trace(new TutorialText($"[Chain {context.ChainId}] {action.DebugName}: {context.Outcome?.Status}", $"[行动链 {context.ChainId}] {TutorialLocalization.ChineseActionName(action.DebugName)}：{TutorialLocalization.ChineseStatus(context.Outcome?.Status)}"));
    }

    internal sealed class TutorialGuardReactor : GameActionReactor<TutorialAttackAction>
    {
        private readonly Action<TutorialText> trace;
        public TutorialGuardReactor(Action<TutorialText> trace) => this.trace = trace;
        public override ReactionTiming Timing => ReactionTiming.BeforeExecution;
        protected override void React(TutorialAttackAction action, ReactionContext context, ReactionResponse response)
        {
            if (action.GuardAllows)
            {
                trace(new TutorialText("[Boss target] Guard passed", "[首领目标] 守卫判定通过"));
                return;
            }

            trace(new TutorialText("[Boss target] Guard prevents attack before children run", "[首领目标] 守卫在子行动运行前阻止了攻击"));
            response.Prevent("Boss guard blocked the attempt");
        }
    }

    internal sealed class TutorialArmorReactor : GameActionReactor<TutorialDamageAction>
    {
        private readonly int armor;
        private readonly Action<TutorialText> trace;
        public TutorialArmorReactor(int armor, Action<TutorialText> trace)
        {
            this.armor = armor;
            this.trace = trace;
        }

        public override ReactionTiming Timing => ReactionTiming.BeforeExecution;
        protected override void React(TutorialDamageAction action, ReactionContext context, ReactionResponse response)
        {
            int original = action.Amount;
            action.SetAmount(TutorialCombatMath.ApplyArmor(original, armor));
            trace(new TutorialText($"[Boss target] Armor: {original} -> {action.Amount}", $"[首领目标] 护甲：{original} → {action.Amount}"));
        }
    }

    internal sealed class TutorialCounterReactor : GameActionReactor<TutorialCheckAction>
    {
        private readonly Action<TutorialText> trace;
        public TutorialCounterReactor(Action<TutorialText> trace) => this.trace = trace;
        public override ReactionTiming Timing => ReactionTiming.AfterResolved;
        public override bool Matches(ReactionContext context) => context.Outcome.HasValue && context.Outcome.Value.Status == ActionStatus.Failed;
        protected override void React(TutorialCheckAction action, ReactionContext context, ReactionResponse response)
        {
            trace(new TutorialText("[Boss target] Failed check triggers immediate counter", "[首领目标] 判定失败，立即触发反击"));
            response.EnqueueImmediate(new TutorialDamageAction((TutorialFighter)action.Target, (TutorialFighter)action.Source, TutorialConsts.FailedCheckCounterDamage, trace), "Boss counter");
        }
    }

    internal sealed class TutorialHealReactor : GameActionReactor<TutorialAttackAction>
    {
        private readonly Action<TutorialText> trace;
        public TutorialHealReactor(Action<TutorialText> trace) => this.trace = trace;
        public override ReactionTiming Timing => ReactionTiming.AfterResolved;
        public override bool Matches(ReactionContext context) => context.Outcome.HasValue && context.Outcome.Value.IsSuccess;
        protected override void React(TutorialAttackAction action, ReactionContext context, ReactionResponse response)
        {
            trace(new TutorialText("[Hero source] Successful attack queues heal", "[玩家来源] 攻击成功，治疗行动入队"));
            response.EnqueueImmediate(new TutorialHealAction((TutorialFighter)action.Source, trace), "Successful attack heal");
        }
    }

    internal static class TutorialCombatMath
    {
        public static int ApplyArmor(int damage, int armor) => Math.Max(0, damage - armor);
    }
}
