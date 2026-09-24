using System;
using System.Threading;
using CardGame.ActionQueue;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CardGame.ActionQueue.Tutorial
{
    public sealed class TutorialBasicScene : MonoBehaviour
    {
        [SerializeField] private ActionQueueRunner runner;
        [SerializeField] private TutorialView view;

        private void Start()
        {
            view.Initialize(new TutorialText("01  /  THE QUEUE", "01  /  行动队列"), new TutorialText("One root action contains a nested composite. Each child resolves before its parent continues. A failed child stops the remaining sequence and returns failure to the root.", "一个根行动包含嵌套的组合行动。每个子行动结算后，父行动才继续；子行动失败会终止后续步骤，并把失败结果返回根行动。"));
            view.SetRules(TutorialRuleText.BasicLocalized);
            view.SetButton(0, new TutorialText("RUN SUCCESS", "运行成功流程"), () => Run(false).Forget());
            view.SetButton(1, new TutorialText("FAIL MIDDLE STEP", "中途判定失败"), () => Run(true).Forget());
            view.SetButton(2, new TutorialText("CLEAR TRACE", "清空记录"), view.ClearHistory);
            view.HideUnusedButtons(3);
            view.SetState(new TutorialText("Queue idle  |  Steps completed: 0", "队列空闲  |  已完成步骤：0"));
        }

        private async UniTask Run(bool failMiddle)
        {
            if (runner.IsRunning)
                return;

            view.ClearHistory();
            int steps = 0;
            Action<TutorialText> trace = line =>
            {
                steps++;
                view.Append(line);
                view.SetState(new TutorialText($"Queue running  |  Steps completed: {steps}", $"队列运行中  |  已完成步骤：{steps}"));
            };
            ActionOutcome outcome = await runner.Enqueue(new TutorialSequenceAction(trace, failMiddle));
            view.Append(new TutorialText($"ROOT => {outcome}", $"根行动 => {TutorialLocalization.ChineseStatus(outcome.Status)}"));
            view.SetState(new TutorialText($"Queue idle  |  Steps completed: {steps}  |  Root: {outcome.Status}", $"队列空闲  |  已完成步骤：{steps}  |  根行动：{TutorialLocalization.ChineseStatus(outcome.Status)}"));
        }
    }

    internal sealed class TutorialSequenceAction : CompositeGameAction
    {
        private readonly Action<TutorialText> trace;
        private readonly bool failMiddle;

        public TutorialSequenceAction(Action<TutorialText> trace, bool failMiddle)
        {
            this.trace = trace;
            this.failMiddle = failMiddle;
        }

        protected override GameAction GetNextChild(CompositeExecutionContext context)
        {
            if (context.CompletedCount == 0)
                return new TutorialStepAction(new TutorialText("1. Choose target", "1. 选择目标"), true, trace);
            if (!context.LastOutcome.IsSuccess)
                return null;
            if (context.CompletedCount == 1)
                return new TutorialInnerAction(trace, failMiddle);
            if (context.CompletedCount == 2)
                return new TutorialStepAction(new TutorialText("4. Finish attack", "4. 完成攻击"), true, trace);
            return null;
        }

        protected override ActionOutcome Resolve(CompositeExecutionContext context)
        {
            return context.CompletedCount == 0 ? ActionOutcome.Failure("No children") : context.LastOutcome;
        }
    }

    internal sealed class TutorialInnerAction : CompositeGameAction
    {
        private readonly Action<TutorialText> trace;
        private readonly bool failMiddle;

        public TutorialInnerAction(Action<TutorialText> trace, bool failMiddle)
        {
            this.trace = trace;
            this.failMiddle = failMiddle;
        }

        protected override GameAction GetNextChild(CompositeExecutionContext context)
        {
            if (context.CompletedCount == 0)
                return new TutorialStepAction(new TutorialText("  2. Roll check", "  2. 力量判定"), !failMiddle, trace);
            if (context.CompletedCount == 1 && context.LastOutcome.IsSuccess)
                return new TutorialStepAction(new TutorialText("  3. Apply damage", "  3. 造成伤害"), true, trace);
            return null;
        }

        protected override ActionOutcome Resolve(CompositeExecutionContext context)
        {
            return context.CompletedCount == 0 ? ActionOutcome.Failure("No children") : context.LastOutcome;
        }
    }

    internal sealed class TutorialStepAction : CommandAction
    {
        private readonly TutorialText label;
        private readonly bool succeeds;
        private readonly Action<TutorialText> trace;

        public TutorialStepAction(TutorialText label, bool succeeds, Action<TutorialText> trace)
        {
            this.label = label;
            this.succeeds = succeeds;
            this.trace = trace;
        }

        public override string DebugName => label.English;

        protected override UniTask<ActionOutcome> ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            trace(new TutorialText($"{label.English} => {(succeeds ? "Succeeded" : "Failed")}", $"{label.Chinese} => {(succeeds ? "成功" : "失败")}"));
            return UniTask.FromResult(succeeds ? ActionOutcome.Success() : ActionOutcome.Failure(label.English));
        }
    }
}
