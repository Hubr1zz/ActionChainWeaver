
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue
{
    /// <summary>
    /// 单线程、无递归的行动队列。
    /// 外部请求进入根 FIFO；当前根流程内部使用双端队列实现 AddToTop/AddToBottom。
    /// </summary>
    public sealed partial class ActionQueueEngine : IDisposable, IActionQueueScheduler
    {
        private readonly Queue<RootRequest> _rootQueue = new();
        private readonly ArrayDeque<QueueWorkItem> _workQueue = new(16);

        private ReactorRegistry _reactors;
        private ReactionGateRegistry _reactionGates;
        private ActionEngineGuardSet _engineGuards;
        private ActiveChain _activeChain;
        private CancellationTokenSource _activeChainCancellation;
        private bool _isRunning;
        private bool _isClearing;
        private bool _isDisposed;
        private long _nextChainId = 1;
        private long _nextActionId = 1;

        public ActionQueueEngine(
            ActionQueueOptions options = null,
            IActionQueueLogger logger = null)
        {
            options ??= new ActionQueueOptions();
            MaxActionsPerChain = options.MaxActionsPerChain;
            TraceCapacity = options.TraceCapacity;
            LogLevel = options.LogLevel;
            SkipPresentationWaits = options.SkipPresentationWaits;
            Logger = logger;
        }

        public ReactorRegistry Reactors
        {
            get
            {
                ThrowIfDisposed();

                if (_reactors != null)
                    return _reactors;

                _reactors = new ReactorRegistry();
                DebugAttachReactorRegistry(_reactors);
                return _reactors;
            }
        }
        public ReactionGateRegistry ReactionGates => _reactionGates ??= new ReactionGateRegistry();
        public ActionEngineGuardSet EngineGuards => _engineGuards ??= new ActionEngineGuardSet();
        public bool IsRunning => _isRunning;
        public int PendingRootCount => _rootQueue.Count;
        public int MaxActionsPerChain { get; }
        public int TraceCapacity { get; }
        public bool SkipPresentationWaits { get; set; }

        /// <summary>
        /// 中止当前 Chain，并取消调用时已经排队的所有根 Action。
        /// 正在执行的异步 Action 需要遵守传入的 CancellationToken 才能立即退出。
        /// </summary>
        public void StopAndClear()
        {
            if (_isDisposed)
                return;

            if (_isClearing)
                return;

            _isClearing = true;
            var cancelled = ActionOutcome.Cancelled("Action queue stopped and cleared.");
            try
            {
                RootRequest[] pendingRequests = _rootQueue.ToArray();
                _rootQueue.Clear();

                if (_activeChain != null)
                {
                    _activeChain.DiscardHistory = true;
                    AbortActiveChain(cancelled);
                }

                try
                {
                    _activeChainCancellation?.Cancel();
                }
                catch (AggregateException exception)
                {
                    LogException(exception);
                }

                foreach (RootRequest request in pendingRequests)
                    request.Completion.TrySetResult(cancelled);

                DebugClearAll();
            }
            finally
            {
                _isClearing = false;
            }
        }
        public UniTask<ActionOutcome> Enqueue(
            GameAction rootAction,
            IReadOnlyList<IGameActionReactor> chainReactors = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (rootAction == null)
                throw new ArgumentNullException(nameof(rootAction));

            if (_isClearing)
            {
                return UniTask.FromResult(
                    ActionOutcome.Cancelled("Action queue is currently being cleared."));
            }

            var completion = new UniTaskCompletionSource<ActionOutcome>();
            _rootQueue.Enqueue(new RootRequest(
                rootAction,
                chainReactors,
                cancellationToken,
                completion));
            DebugNotifyChanged();

            if (!_isRunning)
                RunPumpAsync().Forget();

            return completion.Task;
        }

        void IActionQueueScheduler.EnqueueFromCurrentAction(
            GameAction action,
            bool immediate,
            long parentActionId,
            string cause)
        {
            if (_activeChain == null)
                throw new InvalidOperationException("There is no active action chain.");

            ScheduleAction(action, immediate, parentActionId, cause, null);
        }

        private async UniTask RunPumpAsync()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            try
            {
                while (_rootQueue.Count > 0)
                {
                    RootRequest request = _rootQueue.Dequeue();
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        request.Completion.TrySetResult(ActionOutcome.Cancelled("Cancelled before execution."));
                        continue;
                    }

                    _activeChain = new ActiveChain(
                        _nextChainId++,
                        request,
                        Math.Max(4, TraceCapacity));
                    _activeChainCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        request.CancellationToken);
                    CancellationToken chainCancellationToken = _activeChainCancellation.Token;
                    DebugBeginChain(_activeChain.Id, request.ChainReactors);

                    try
                    {
                        ScheduleAction(
                            request.RootAction,
                            true,
                            0,
                            "Root",
                            outcome => _activeChain.RootOutcome = outcome);
                    }
                    catch (Exception exception)
                    {
                        LogException(exception);
                        AbortActiveChain(ActionOutcome.Failure(
                            $"Root action could not be scheduled: {exception.Message}"));
                    }

                    while (_workQueue.Count > 0 && !_activeChain.IsAborted)
                    {
                        if (chainCancellationToken.IsCancellationRequested)
                        {
                            AbortActiveChain(ActionOutcome.Cancelled("Root cancellation requested."));
                            break;
                        }

                        QueueWorkItem workItem = RemoveFirstWorkItem();
                        try
                        {
                            await DebugBeforeWorkItemAsync(workItem, chainCancellationToken);
                            if (_activeChain.IsAborted)
                                break;

                            await workItem.RunAsync(this, chainCancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            AbortActiveChain(ActionOutcome.Cancelled("Action wait was cancelled."));
                        }
                        catch (Exception exception)
                        {
                            LogException(exception);
                            AbortActiveChain(ActionOutcome.Failure(
                                $"Unhandled exception in {workItem.DebugName}."));
                        }
                    }

                    ActionOutcome rootOutcome = _activeChain.IsAborted
                        ? _activeChain.AbortOutcome
                        : _activeChain.RootOutcome ?? ActionOutcome.Failure("Root action produced no outcome.");

                    request.Completion.TrySetResult(rootOutcome);
                    if (_activeChain.DiscardHistory)
                        DebugClearAll();
                    else
                        DebugCompleteChain(_activeChain.ExecutedActionCount);

                    _activeChainCancellation.Dispose();
                    _activeChainCancellation = null;
                    _workQueue.Clear();
                    _activeChain = null;
                }
            }
            finally
            {
                _workQueue.Clear();
                _activeChainCancellation?.Dispose();
                _activeChainCancellation = null;
                _activeChain = null;
                _isRunning = false;
                DebugStop();
            }
        }

        private void AddWorkItem(QueueWorkItem workItem, bool immediate)
        {
            if (immediate)
                _workQueue.AddFirst(workItem);
            else
                _workQueue.AddLast(workItem);
        }

        private QueueWorkItem RemoveFirstWorkItem()
        {
            return _workQueue.RemoveFirst();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            StopAndClear();
            _isDisposed = true;
            DebugDispose();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ActionQueueEngine));
        }

    }
}
