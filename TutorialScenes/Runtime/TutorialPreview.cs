using System;
using System.Collections.Generic;
using CardGame.ActionQueue;
using Cysharp.Threading.Tasks;
using GameFramework.Preview;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.ActionQueue.Tutorial
{
    public sealed class TutorialPreviewScene : MonoBehaviour
    {
        [SerializeField] private ActionQueueRunner runner;
        [SerializeField] private TutorialView view;
        [SerializeField] private TutorialAttackCardDrag attackCard;
        [SerializeField] private TutorialPreviewTargetView slimeTargetView;
        [SerializeField] private TutorialPreviewTargetView bossTargetView;
        [SerializeField] private Text previewFeedbackText;

        private readonly List<IDisposable> registrations = new();
        private PreviewPipeline<TutorialPreviewInput, int> pipeline;
        private SimulationPreview<TutorialPreviewNode> simulation;
        private TutorialFighter hero;
        private TutorialFighter boss;
        private TutorialFighter slime;
        private bool buffActive;
        private TutorialPreviewTargetView hoveredTarget;

        private void Start()
        {
            hero = new TutorialFighter("Hero", TutorialConsts.HeroMaxHp);
            boss = new TutorialFighter("Boss", TutorialConsts.BossMaxHp);
            slime = new TutorialFighter("Slime", TutorialConsts.SlimeMaxHp);
            boss.SetBlock(TutorialConsts.PreviewBossBlock);
            pipeline = new PreviewPipeline<TutorialPreviewInput, int>(input => input.BaseDamage);
            registrations.Add(pipeline.Register(new TutorialBonusPreviewRule()));
            registrations.Add(pipeline.Register(new TutorialBlockPreviewRule()));
            simulation = new SimulationPreview<TutorialPreviewNode>(new TutorialPreviewExpander());
            registrations.Add(runner.Reactors.RegisterForEntity(boss, new TutorialBlockReactor(LogAndRefresh), ReactorRelation.Target));
            registrations.Add(runner.Reactors.RegisterForEntity(boss, new TutorialRandomCounterReactor(hero, boss, LogAndRefresh), ReactorRelation.Target));

            view.Initialize(new TutorialText("04  /  DRAG TO PREVIEW", "04  /  拖拽预览"), new TutorialText("Drag the Attack card over Slime or Boss. The flashing part of a health bar is predicted damage only; Block can prevent it. Release over a target to commit the real Action, or drag away to cancel.", "把攻击牌拖到史莱姆或首领上。血条闪烁部分只是预计损失，格挡可能抵消伤害。对准目标松手才会执行真正的行动；拖离目标则取消。"));
            view.SetButton(0, new TutorialText("TOGGLE +2 BUFF", "切换＋2加成"), ToggleBuff);
            view.SetButton(1, new TutorialText("RESET BOARD", "重置战场"), ResetState);
            view.HideUnusedButtons(2);
            view.SetExternalText(previewFeedbackText, new TutorialText("Drag ATTACK onto a target to preview; release to play.", "将攻击牌拖到目标上查看预览，松手后执行。"));
            view.LanguageChanged += OnLanguageChanged;
            UpdateState();
        }

        private void ToggleBuff()
        {
            if (runner.IsRunning)
                return;

            buffActive = !buffActive;
            ClearHoverPreview();
            view.SetExternalText(previewFeedbackText, new TutorialText("Buff changed. Drag ATTACK again for a fresh preview.", "加成已变化，请重新拖动攻击牌查看预览。"));
            UpdateState();
        }

        public void ShowHoverPreview(TutorialPreviewTargetView targetView)
        {
            if (runner.IsRunning)
                return;
            if (ReferenceEquals(hoveredTarget, targetView))
                return;

            ClearHoverPreview();
            if (targetView == null)
                return;
            if (!ReferenceEquals(targetView, bossTargetView) && !ReferenceEquals(targetView, slimeTargetView))
                return;

            bool isBoss = ReferenceEquals(targetView, bossTargetView);
            TutorialFighter target = isBoss ? boss : slime;
            if (target.Hp == 0)
            {
                view.SetExternalText(previewFeedbackText, new TutorialText($"{target.ReactorName} is already defeated.", $"{TutorialLocalization.ChineseEntityName(target.ReactorName)}已被击败。"));
                return;
            }

            hoveredTarget = targetView;
            var input = new TutorialPreviewInput(TutorialConsts.AttackDamage, buffActive ? TutorialConsts.BuffBonus : 0, target.Block);
            PreviewResult<int> calculation = pipeline.Evaluate(input);
            int rawDamage = input.BaseDamage + input.Bonus;
            int blockedDamage = rawDamage - calculation.Value;
            var root = new TutorialPreviewNode(TutorialPreviewNodeKind.Root, target.ReactorName, calculation.Value, isBoss && calculation.Value > 0 && boss.Hp > calculation.Value);
            SimulationPreviewResult<TutorialPreviewNode> forecast = simulation.Build(root);
            UpdateState();
            targetView.ShowPreview(target.Hp, target.MaxHp, calculation.Value, blockedDamage);
            if (blockedDamage >= rawDamage)
                view.SetExternalText(previewFeedbackText, new TutorialText($"Your {rawDamage} damage is fully blocked because {target.ReactorName} has {target.Block} Block.", $"你的 {rawDamage} 点伤害将被格挡，因为{TutorialLocalization.ChineseEntityName(target.ReactorName)}有 {target.Block} 点格挡。"));
            else if (blockedDamage > 0)
                view.SetExternalText(previewFeedbackText, new TutorialText($"{blockedDamage} damage blocked; you will deal {calculation.Value} damage.", $"{blockedDamage} 点伤害被格挡；你将造成 {calculation.Value} 点伤害。"));
            else
                view.SetExternalText(previewFeedbackText, new TutorialText($"You will deal {calculation.Value} damage.", $"你将要造成 {calculation.Value} 点伤害。"));
            view.ClearHistory();
            view.Append(new TutorialText($"PREVIEW {target.ReactorName}: no HP or Block changed", $"预览{TutorialLocalization.ChineseEntityName(target.ReactorName)}：生命和格挡尚未改变"));
            foreach (PreviewTraceEntry entry in calculation.Trace.Entries)
                view.Append(new TutorialText($"  {entry.RuleId}: {entry.Message}", $"  {ChinesePreviewRuleName(entry.RuleId)}：{entry.Message}"));
            IReadOnlyList<PlayerPreviewLine> lines = PlayerPreviewFormatter.Format(forecast);
            for (int i = 0; i < lines.Count; i++)
            {
                PlayerPreviewLine line = lines[i];
                TutorialPreviewNode node = forecast.Root.Children[i].Value;
                string english = string.IsNullOrEmpty(line.Note) ? $"  {line.Text}" : $"  {line.Text} ({line.Note})";
                string chinese = node.Kind == TutorialPreviewNodeKind.Damage ? $"  将对{TutorialLocalization.ChineseEntityName(node.TargetName)}造成 {node.Damage} 点伤害" : "  首领可能反击 1 点伤害（50% 概率；提交前无法确定）";
                view.Append(new TutorialText(english, chinese));
            }
        }

        public void ClearHoverPreview()
        {
            hoveredTarget = null;
            slimeTargetView.ClearPreview();
            bossTargetView.ClearPreview();
            view.SetExternalText(previewFeedbackText, new TutorialText("Drag ATTACK onto a target to preview; release to play.", "将攻击牌拖到目标上查看预览，松手后执行。"));
            UpdateState();
        }

        public void DropOnTarget(TutorialPreviewTargetView targetView)
        {
            ClearHoverPreview();
            if (targetView == null || runner.IsRunning)
                return;
            if (!ReferenceEquals(targetView, bossTargetView) && !ReferenceEquals(targetView, slimeTargetView))
                return;

            TutorialFighter target = ReferenceEquals(targetView, bossTargetView) ? boss : slime;
            if (target.Hp == 0)
            {
                view.SetExternalText(previewFeedbackText, new TutorialText($"{target.ReactorName} is already defeated.", $"{TutorialLocalization.ChineseEntityName(target.ReactorName)}已被击败。"));
                return;
            }

            Commit(target).Forget();
        }

        private async UniTask Commit(TutorialFighter target)
        {
            if (runner.IsRunning)
                return;

            int rawDamage = TutorialConsts.AttackDamage + (buffActive ? TutorialConsts.BuffBonus : 0);
            view.SetExternalText(previewFeedbackText, new TutorialText($"Playing ATTACK on {target.ReactorName}...", $"正在对{TutorialLocalization.ChineseEntityName(target.ReactorName)}执行攻击……"));
            view.Append(new TutorialText($"COMMIT: live {target.ReactorName} Block {target.Block}, raw damage {rawDamage}", $"提交：{TutorialLocalization.ChineseEntityName(target.ReactorName)}当前格挡 {target.Block}，原始伤害 {rawDamage}"));
            ActionOutcome outcome = await runner.Enqueue(new TutorialDamageAction(hero, target, rawDamage, LogAndRefresh));
            view.Append(new TutorialText($"ACTION => {outcome.Status}", $"行动 => {TutorialLocalization.ChineseStatus(outcome.Status)}"));
            view.SetExternalText(previewFeedbackText, new TutorialText($"ATTACK resolved on {target.ReactorName}: {outcome.Status}. Drag again to preview the new state.", $"对{TutorialLocalization.ChineseEntityName(target.ReactorName)}的攻击已结算：{TutorialLocalization.ChineseStatus(outcome.Status)}。再次拖动可预览新状态。"));
            UpdateState();
        }

        private void ResetState()
        {
            if (runner.IsRunning)
                return;

            hero.Restore(TutorialConsts.HeroMaxHp);
            boss.Restore(TutorialConsts.BossMaxHp);
            slime.Restore(TutorialConsts.SlimeMaxHp);
            boss.SetBlock(TutorialConsts.PreviewBossBlock);
            buffActive = false;
            attackCard.ResetToHome();
            ClearHoverPreview();
            view.ClearHistory();
            UpdateState();
        }

        private void LogAndRefresh(TutorialText line)
        {
            view.Append(line);
            UpdateState();
        }

        private void UpdateState()
        {
            view.SetState(new TutorialText($"ATTACK {TutorialConsts.AttackDamage}    |    Buff: {(buffActive ? $"+{TutorialConsts.BuffBonus}" : "off")}    |    Boss Block: {boss.Block}", $"攻击 {TutorialConsts.AttackDamage}    |    加成：{(buffActive ? $"+{TutorialConsts.BuffBonus}" : "关闭")}    |    首领格挡：{boss.Block}"));
            slimeTargetView.SetState(slime.ReactorName, slime.Hp, slime.MaxHp, slime.Block);
            bossTargetView.SetState(boss.ReactorName, boss.Hp, boss.MaxHp, boss.Block);
            view.SetRules(TutorialRuleText.PreviewLocalized(boss.Block));
        }

        private void OnLanguageChanged()
        {
            TutorialPreviewTargetView target = hoveredTarget;
            if (target == null)
            {
                UpdateState();
                return;
            }

            hoveredTarget = null;
            ShowHoverPreview(target);
        }

        private static string ChinesePreviewRuleName(string ruleId)
        {
            if (ruleId == "Hero buff")
                return "玩家加成";
            if (ruleId == "Target Block")
                return "目标格挡";
            return ruleId;
        }

        private void OnDestroy()
        {
            if (view != null)
                view.LanguageChanged -= OnLanguageChanged;
            foreach (IDisposable registration in registrations)
                registration.Dispose();
        }
    }

    internal readonly struct TutorialPreviewInput
    {
        public TutorialPreviewInput(int baseDamage, int bonus, int block)
        {
            BaseDamage = baseDamage;
            Bonus = bonus;
            Block = block;
        }

        public int BaseDamage { get; }
        public int Bonus { get; }
        public int Block { get; }
    }

    internal sealed class TutorialBonusPreviewRule : IPreviewRule<TutorialPreviewInput, int>
    {
        public string Id => "Hero buff";
        public int Priority => 100;

        public int Evaluate(TutorialPreviewInput input, int current, PreviewTrace trace)
        {
            int result = current + input.Bonus;
            trace.Add(Id, $"{current} + {input.Bonus} = {result}");
            return result;
        }
    }

    internal sealed class TutorialBlockPreviewRule : IPreviewRule<TutorialPreviewInput, int>
    {
        public string Id => "Target Block";
        public int Priority => 50;

        public int Evaluate(TutorialPreviewInput input, int current, PreviewTrace trace)
        {
            int result = TutorialCombatMath.ApplyArmor(current, input.Block);
            trace.Add(Id, $"{current} - {Math.Min(current, input.Block)} = {result}");
            return result;
        }
    }

    internal enum TutorialPreviewNodeKind
    {
        Root,
        Damage,
        PossibleCounter
    }

    internal readonly struct TutorialPreviewNode
    {
        public TutorialPreviewNode(TutorialPreviewNodeKind kind, string targetName, int damage, bool mayCounter)
        {
            Kind = kind;
            TargetName = targetName;
            Damage = damage;
            MayCounter = mayCounter;
        }

        public TutorialPreviewNodeKind Kind { get; }
        public string TargetName { get; }
        public int Damage { get; }
        public bool MayCounter { get; }
    }

    internal sealed class TutorialPreviewExpander : ISimulationExpander<TutorialPreviewNode>
    {
        public SimulationExpansion<TutorialPreviewNode> Expand(TutorialPreviewNode node)
        {
            if (node.Kind == TutorialPreviewNodeKind.Root)
            {
                var children = new List<TutorialPreviewNode>
                {
                    new(TutorialPreviewNodeKind.Damage, node.TargetName, node.Damage, node.MayCounter)
                };
                if (node.MayCounter)
                    children.Add(new TutorialPreviewNode(TutorialPreviewNodeKind.PossibleCounter, node.TargetName, 0, true));
                return new SimulationExpansion<TutorialPreviewNode>("Attack", $"Attack {node.TargetName}", children);
            }

            if (node.Kind == TutorialPreviewNodeKind.Damage)
                return new SimulationExpansion<TutorialPreviewNode>("Damage", "Deterministic incoming damage", disclosure: PreviewDisclosure.NumericChange($"Incoming damage to {node.TargetName}: {node.Damage}"));

            return new SimulationExpansion<TutorialPreviewNode>("Counter", "Random response", uncertainty: new SimulationUncertainty(SimulationUncertaintyKind.RandomOutcome, "50% chance; outcome is unknown before commit"), disclosure: PreviewDisclosure.Trigger("Boss may counterattack for 1 damage"));
        }
    }

    internal sealed class TutorialRandomCounterReactor : GameActionReactor<TutorialDamageAction>
    {
        private readonly TutorialFighter hero;
        private readonly TutorialFighter boss;
        private readonly Action<TutorialText> trace;

        public TutorialRandomCounterReactor(TutorialFighter hero, TutorialFighter boss, Action<TutorialText> trace)
        {
            this.hero = hero;
            this.boss = boss;
            this.trace = trace;
        }

        public override ReactionTiming Timing => ReactionTiming.AfterResolved;
        public override bool Matches(ReactionContext context) => context.Outcome.HasValue && context.Outcome.Value.IsSuccess && boss.Hp > 0 && ((TutorialDamageAction)context.Action).Amount > 0;

        protected override void React(TutorialDamageAction action, ReactionContext context, ReactionResponse response)
        {
            if (UnityEngine.Random.value >= 0.5f)
            {
                trace(new TutorialText("Boss counter roll: no counter", "首领反击判定：未触发"));
                return;
            }

            trace(new TutorialText("Boss counter roll: triggered", "首领反击判定：已触发"));
            response.EnqueueImmediate(new TutorialDamageAction(boss, hero, TutorialConsts.PreviewCounterDamage, trace), "Random boss counter");
        }
    }
}
