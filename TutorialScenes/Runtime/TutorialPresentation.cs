using System;
using System.Collections.Generic;
using System.Threading;
using CardGame.ActionQueue;
using Cysharp.Threading.Tasks;
using GameFramework.Presentation;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace CardGame.ActionQueue.Tutorial
{
    public sealed class TutorialPresentationScene : MonoBehaviour
    {
        [SerializeField] private ActionQueueRunner runner;
        [SerializeField] private TutorialView view;
        [SerializeField] private Image playerBlock;
        [SerializeField, FormerlySerializedAs("effectCard")] private Image enemyBlock;
        [SerializeField] private RectTransform impactMarker;
        [SerializeField] private TutorialPresentationQueueView queueView;
        [SerializeField] private Color enemyHitColor = new(1f, 0.3f, 0.24f, 1f);

        private PresentationDispatcher dispatcher;
        private RootActionInputGate inputGate;
        private IDisposable registration;
        private TutorialHitMotionHandler motionHandler;
        private readonly List<int> unfinishedActions = new();
        private readonly List<PresentationHandle> unfinishedMotions = new();
        private int submittedAttacks;
        private int finishedActions;
        private int submittedMotions;
        private int finishedMotions;
        private int rejectedInputs;
        private int dummyHp = TutorialConsts.BossMaxHp;

        private void Start()
        {
            dispatcher = new PresentationDispatcher();
            inputGate = new RootActionInputGate(() => runner.IsRunning || runner.PendingRootCount > 0, () => dispatcher.HasActiveRequests);
            motionHandler = new TutorialHitMotionHandler(playerBlock, enemyBlock, impactMarker, enemyHitColor, view.Append);
            registration = dispatcher.Register(motionHandler);
            view.Initialize(new TutorialText("03  /  PRESENTATION", "03  /  表现队列"), new TutorialText("Each attack changes HP and publishes a separate hit motion. SKIP WAIT lets logic finish while motion continues. Cycle INPUT LOCK to reject new root attacks while logic or visuals are busy; triggered chain effects are unaffected.", "每次攻击都会扣血并发布独立的撞击表现。开启跳过等待后，逻辑先结束、表现继续播放。切换输入锁可在逻辑或表现忙时拒绝新的根行动；连携效果不受影响。"));
            view.SetRules(TutorialRuleText.PresentationLocalized);
            view.SetButton(0, new TutorialText("HIT / SUBMIT ROOT", "攻击／提交根行动"), () => Run().Forget());
            view.SetButton(1, new TutorialText("TOGGLE SKIP WAIT", "切换跳过等待"), ToggleSkipWait);
            view.SetButton(2, InputLockButtonLabel, CycleInputLock);
            view.SetButton(3, new TutorialText("TOGGLE INSTANT FX", "切换立即表现"), ToggleInstant);
            view.SetButton(4, new TutorialText("RESET DEMO", "重置演示"), ResetState);
            view.HideUnusedButtons(5);
            view.LanguageChanged += RenderQueue;
            UpdateState();
            RenderQueue();
        }

        private void Update()
        {
            RefreshQueue();
        }

        private async UniTask Run()
        {
            RootInputBlockers blockedBy = inputGate.GetBlockingReasons();
            if (blockedBy != RootInputBlockers.None)
            {
                rejectedInputs++;
                view.Append(new TutorialText($"INPUT REJECTED: {blockedBy}. No new root Action was queued.", $"输入被拒绝：{TutorialLocalization.ChineseInputBlockers(blockedBy)}。没有新的根行动入队。"));
                UpdateState();
                return;
            }

            int attackId = ++submittedAttacks;
            unfinishedActions.Add(attackId);
            view.Append(new TutorialText($"Queue attack #{attackId} | skip presentation wait: {runner.SkipPresentationWaits}", $"攻击 #{attackId} 入队｜跳过表现等待：{(runner.SkipPresentationWaits ? "是" : "否")}"));
            RenderQueue();
            ActionOutcome outcome = await runner.Enqueue(new TutorialPresentedDamageAction(dispatcher, enemyBlock, TutorialConsts.PresentedDamage, ApplyDamage, OnMotionPublished, view.Append));
            unfinishedActions.Remove(attackId);
            finishedActions++;
            view.Append(new TutorialText($"ATTACK #{attackId} => {outcome.Status}", $"攻击 #{attackId} => {TutorialLocalization.ChineseStatus(outcome.Status)}"));
            UpdateState();
            RenderQueue();
        }

        private void ApplyDamage(int amount)
        {
            dummyHp = Math.Max(0, dummyHp - amount);
            view.Append(new TutorialText($"Authoritative HP changed: -{amount}", $"权威生命值减少 {amount} 点"));
            UpdateState();
        }

        private void ToggleInstant()
        {
            RefreshQueue();
            if (runner.IsRunning || dispatcher.HasActiveRequests)
            {
                view.Append(new TutorialText("Wait for queued Actions and motions before switching execution mode.", "请等待队列中的行动和表现结束后再切换执行模式。"));
                return;
            }

            dispatcher.ExecutionMode = dispatcher.ExecutionMode == PresentationExecutionMode.Normal ? PresentationExecutionMode.CompleteImmediately : PresentationExecutionMode.Normal;
            UpdateState();
        }

        private void ToggleSkipWait()
        {
            if (runner.IsRunning)
            {
                view.Append(new TutorialText("Wait for the current root Action before changing skip-wait mode.", "请等待当前根行动结束后再切换跳过等待模式。"));
                return;
            }

            runner.SkipPresentationWaits = !runner.SkipPresentationWaits;
            UpdateState();
        }

        private void CycleInputLock()
        {
            switch (inputGate.BlockWhen)
            {
                case RootInputBlockers.None:
                    inputGate.BlockWhen = RootInputBlockers.PresentationBusy;
                    break;
                case RootInputBlockers.PresentationBusy:
                    inputGate.BlockWhen = RootInputBlockers.LogicBusy;
                    break;
                case RootInputBlockers.LogicBusy:
                    inputGate.BlockWhen = RootInputBlockers.LogicBusy | RootInputBlockers.PresentationBusy;
                    break;
                default:
                    inputGate.BlockWhen = RootInputBlockers.None;
                    break;
            }

            view.SetButton(2, InputLockButtonLabel, CycleInputLock);
            UpdateState();
        }

        private TutorialText InputLockLabel => inputGate.BlockWhen switch
        {
            RootInputBlockers.PresentationBusy => new TutorialText("VISUAL", "表现忙"),
            RootInputBlockers.LogicBusy => new TutorialText("LOGIC", "逻辑忙"),
            RootInputBlockers.LogicBusy | RootInputBlockers.PresentationBusy => new TutorialText("BOTH", "两者"),
            _ => new TutorialText("OFF", "关闭")
        };

        private TutorialText InputLockButtonLabel => new($"INPUT LOCK: {InputLockLabel.English}", $"输入锁：{InputLockLabel.Chinese}");

        private void ResetState()
        {
            if (runner.IsRunning)
            {
                view.Append(new TutorialText("Wait for the current root Action before resetting the demo.", "请等待当前根行动结束后再重置演示。"));
                return;
            }
            RefreshQueue();
            if (dispatcher.HasActiveRequests)
            {
                view.Append(new TutorialText("Wait for queued motions to finish before resetting HP.", "请等待队列中的表现结束后再重置生命值。"));
                return;
            }

            dummyHp = TutorialConsts.BossMaxHp;
            submittedAttacks = 0;
            finishedActions = 0;
            submittedMotions = 0;
            finishedMotions = 0;
            rejectedInputs = 0;
            view.ClearHistory();
            UpdateState();
            RenderQueue();
        }

        private void UpdateState()
        {
            view.SetState(new TutorialText($"Foe HP {dummyHp}/{TutorialConsts.BossMaxHp}  |  Motion: {dispatcher.ExecutionMode}\nSkip wait: {runner.SkipPresentationWaits}  |  Input lock: {InputLockLabel.English}  |  Rejected: {rejectedInputs}", $"敌人生命 {dummyHp}/{TutorialConsts.BossMaxHp}  |  表现：{(dispatcher.ExecutionMode == PresentationExecutionMode.Normal ? "正常" : "立即完成")}\n跳过等待：{(runner.SkipPresentationWaits ? "是" : "否")}  |  输入锁：{InputLockLabel.Chinese}  |  已拒绝：{rejectedInputs}"));
        }

        private void OnMotionPublished(PresentationHandle handle)
        {
            submittedMotions++;
            if (handle.IsFinished)
                finishedMotions++;
            else
                unfinishedMotions.Add(handle);
            RenderQueue();
        }

        private void RefreshQueue()
        {
            bool changed = false;
            for (int i = unfinishedMotions.Count - 1; i >= 0; i--)
            {
                if (!unfinishedMotions[i].IsFinished)
                    continue;

                unfinishedMotions.RemoveAt(i);
                finishedMotions++;
                changed = true;
            }

            if (changed)
                RenderQueue();
        }

        private void RenderQueue()
        {
            queueView.Render(submittedAttacks, finishedActions, unfinishedActions, submittedMotions, finishedMotions, unfinishedMotions);
        }

        private void OnDestroy()
        {
            registration?.Dispose();
            dispatcher?.Dispose();
            if (view != null)
                view.LanguageChanged -= RenderQueue;
        }
    }

    internal sealed class TutorialHitMotionRequest : PresentationRequest
    {
        public TutorialHitMotionRequest(object owner) : base(new PresentationChannel(owner, "AttackMotion"), PresentationConflictPolicy.Queue)
        {
        }
    }

    internal sealed class TutorialHitMotionHandler : PresentationHandler<TutorialHitMotionRequest>
    {
        private readonly RectTransform playerRect;
        private readonly RectTransform impactMarker;
        private readonly Image enemyBlock;
        private readonly Color enemyHitColor;
        private readonly Action<TutorialText> trace;
        private readonly Color enemyRestingColor;
        private readonly Vector2 playerHome;

        public TutorialHitMotionHandler(Image playerBlock, Image enemyBlock, RectTransform impactMarker, Color enemyHitColor, Action<TutorialText> trace)
        {
            playerRect = playerBlock.rectTransform;
            this.enemyBlock = enemyBlock;
            this.impactMarker = impactMarker;
            this.enemyHitColor = enemyHitColor;
            this.trace = trace;
            enemyRestingColor = enemyBlock.color;
            playerHome = playerRect.anchoredPosition;
        }

        protected override async UniTask PresentAsync(TutorialHitMotionRequest request, CancellationToken cancellationToken)
        {
            trace(new TutorialText("Motion: Hero approaches Foe", "表现：玩家冲向敌人"));
            try
            {
                await MovePlayerAsync(playerHome, impactMarker.anchoredPosition, TutorialConsts.ApproachSeconds, cancellationToken);
                trace(new TutorialText("Motion: impact; Foe flashes from damage", "表现：撞击，敌人受伤闪烁"));
                await FlashEnemyAsync(cancellationToken);
                trace(new TutorialText("Motion: Hero returns home", "表现：玩家返回原位"));
                await MovePlayerAsync(impactMarker.anchoredPosition, playerHome, TutorialConsts.ReturnSeconds, cancellationToken);
            }
            finally
            {
                if (playerRect != null)
                    playerRect.anchoredPosition = playerHome;
                if (enemyBlock != null)
                    enemyBlock.color = enemyRestingColor;
            }

            trace(new TutorialText("Motion completed", "表现完成"));
        }

        protected override void CompleteImmediately(TutorialHitMotionRequest request)
        {
            playerRect.anchoredPosition = playerHome;
            enemyBlock.color = enemyRestingColor;
            trace(new TutorialText("Motion completed immediately at final visual state", "表现已立即推进到最终画面"));
        }

        private async UniTask MovePlayerAsync(Vector2 from, Vector2 to, float duration, CancellationToken cancellationToken)
        {
            float elapsed = 0;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                playerRect.anchoredPosition = Vector2.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            playerRect.anchoredPosition = to;
        }

        private async UniTask FlashEnemyAsync(CancellationToken cancellationToken)
        {
            float elapsed = 0;
            while (elapsed < TutorialConsts.ImpactSeconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                float pulse = Mathf.Abs(Mathf.Sin(Mathf.Clamp01(elapsed / TutorialConsts.ImpactSeconds) * Mathf.PI * 3f));
                enemyBlock.color = Color.Lerp(enemyRestingColor, enemyHitColor, pulse);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            enemyBlock.color = enemyRestingColor;
        }
    }

    internal sealed class TutorialPresentedDamageAction : CommandAction
    {
        private readonly PresentationDispatcher dispatcher;
        private readonly object channelOwner;
        private readonly int damage;
        private readonly Action<int> applyDamage;
        private readonly Action<PresentationHandle> onPublished;
        private readonly Action<TutorialText> trace;

        public TutorialPresentedDamageAction(PresentationDispatcher dispatcher, object channelOwner, int damage, Action<int> applyDamage, Action<PresentationHandle> onPublished, Action<TutorialText> trace)
        {
            this.dispatcher = dispatcher;
            this.channelOwner = channelOwner;
            this.damage = damage;
            this.applyDamage = applyDamage;
            this.onPublished = onPublished;
            this.trace = trace;
        }

        public override string DebugName => "Damage + hit flash";

        protected override async UniTask<ActionOutcome> ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken)
        {
            applyDamage(damage);
            PresentationHandle handle = dispatcher.Publish(new TutorialHitMotionRequest(channelOwner), cancellationToken);
            onPublished(handle);
            trace(context.SkipPresentationWaits ? new TutorialText("Queue skips presentation wait; motion continues independently", "队列跳过表现等待；动画独立继续") : new TutorialText("Action waiting for presentation handle", "行动正在等待表现句柄"));
            await context.AwaitPresentationAsync(handle.WaitForCompletionAsync());

            return ActionOutcome.Success();
        }
    }
}
