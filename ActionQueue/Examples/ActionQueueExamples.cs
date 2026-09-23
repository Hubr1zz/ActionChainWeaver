using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CardGame.ActionQueue.Examples
{
    [RequireComponent(typeof(ActionQueueRunner))]
    public sealed class ActionQueueExamples : MonoBehaviour
    {
        private ActionQueueRunner _queue;

        private void Awake()
        {
            _queue = GetComponent<ActionQueueRunner>();
        }

        [ContextMenu("ActionQueue/1 Successful attack and heal")]
        private void RunSuccessfulAttackAndHeal()
        {
            RunAttackCase(bossGuardSucceeds: true, strengthSucceeds: true).Forget();
        }

        [ContextMenu("ActionQueue/2 Boss prevents attack")]
        private void RunBossPreventsAttack()
        {
            RunAttackCase(bossGuardSucceeds: false, strengthSucceeds: true).Forget();
        }

        [ContextMenu("ActionQueue/3 Strength failure causes counter")]
        private void RunStrengthFailureCounter()
        {
            RunAttackCase(bossGuardSucceeds: true, strengthSucceeds: false).Forget();
        }

        [ContextMenu("ActionQueue/4 Different target ignores Boss reactors")]
        private void RunDifferentTarget()
        {
            RunAttackCase(
                bossGuardSucceeds: false,
                strengthSucceeds: true,
                attackBoss: false).Forget();
        }

        [ContextMenu("ActionQueue/5 Indirect Damage-Draw loop guard")]
        private void RunIndirectLoopGuard()
        {
            RunLoopCase().Forget();
        }

        private async UniTask RunAttackCase(
            bool bossGuardSucceeds,
            bool strengthSucceeds,
            bool attackBoss = true)
        {
            var hero = new Combatant("Hero", 20);
            hero.TakeDamage(5);
            var boss = new Combatant("Boss", 30);
            var otherEnemy = new Combatant("Slime", 12);

            // Source 实体 Reactor：只响应 hero 发起的 Attack。
            IDisposable heal = _queue.Reactors.RegisterForEntity(
                hero,
                new HealAfterAttackReactor(1),
                ReactorRelation.Source);

            // Target 实体 Reactor：Slime 没有这些 Reactor，因此攻击不同敌人会得到不同结算。
            IDisposable guard = _queue.Reactors.RegisterForEntity(
                boss,
                new BossGuardReactor(bossGuardSucceeds),
                ReactorRelation.Target);

            IDisposable counter = _queue.Reactors.RegisterForEntity(
                boss,
                new CounterOnStrengthFailureReactor(2),
                ReactorRelation.Target);

            IDisposable armor = _queue.Reactors.RegisterForEntity(
                boss,
                new FlatDamageReductionReactor(2),
                ReactorRelation.Target);

            try
            {
                var root = new AttackFlowAction(
                    hero,
                    attackBoss
                        ? new List<Combatant> { boss, otherEnemy }
                        : new List<Combatant> { otherEnemy, boss },
                    6,
                    strengthSucceeds);

                // Chain Reactor：只存在于这次“出牌/事件”产生的整个根流程。
                var chainReactors = new IGameActionReactor[]
                {
                    new ChainAttackLoggerReactor()
                };

                ActionOutcome result = await _queue.Enqueue(root, chainReactors);
                Debug.Log($"[Example] Root={result}; {hero}; {boss}; {otherEnemy}.");
            }
            finally
            {
                heal.Dispose();
                guard.Dispose();
                counter.Dispose();
                armor.Dispose();
            }
        }

        private async UniTask RunLoopCase()
        {
            var hero = new Combatant("LoopHero", 20);
            var enemy = new Combatant("LoopDummy", 999);
            var deck = new DeckState("LoopDeck");

            IDisposable drawOnDamage = _queue.Reactors.RegisterGlobal(new DrawOnDamageReactor(deck));
            IDisposable damageOnDraw = _queue.Reactors.RegisterGlobal(
                new DamageOnDrawReactor(hero, enemy));

            try
            {
                // Damage -> Draw -> Damage -> ...。不会递归压栈；达到链预算后中止并打印最近轨迹。
                ActionOutcome result = await _queue.Enqueue(new DamageAction(hero, enemy, 1));
                Debug.Log($"[Example] Indirect loop ended with {result}.");
            }
            finally
            {
                drawOnDamage.Dispose();
                damageOnDraw.Dispose();
            }
        }
    }
}
