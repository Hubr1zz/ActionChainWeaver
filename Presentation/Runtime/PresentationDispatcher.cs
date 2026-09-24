using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework.Presentation
{
    /// <summary>
    /// 纯 C# 表现调度器。请求按具体类型路由到 Handler，并在每个 Channel 内应用冲突策略。
    /// Publish 与 Handler 默认在 Unity 主线程使用；内部锁只保护取消回调与完成回调的竞态。
    /// </summary>
    public sealed class PresentationDispatcher : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<Type, IPresentationHandler> _handlers = new();
        private readonly Dictionary<PresentationChannel, ChannelState> _channels = new();
        private long _nextId = 1;
        private bool _isDisposed;
        private PresentationExecutionMode _executionMode;

        public PresentationDispatcher(
            PresentationExecutionMode executionMode = PresentationExecutionMode.Normal)
        {
            ExecutionMode = executionMode;
        }

        /// <summary>
        /// Debug 时可切换为 CompleteImmediately。模式在 Publish 时取快照；
        /// 已经发布的表现继续遵循其原有生命周期。
        /// </summary>
        public PresentationExecutionMode ExecutionMode
        {
            get
            {
                lock (_gate)
                    return _executionMode;
            }
            set
            {
                if (value < PresentationExecutionMode.Normal ||
                    value > PresentationExecutionMode.CompleteImmediately)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                lock (_gate)
                {
                    ThrowIfDisposed();
                    _executionMode = value;
                }
            }
        }

        /// <summary>任一 Channel 有正在播放或排队的表现请求。</summary>
        public bool HasActiveRequests
        {
            get
            {
                lock (_gate)
                    return _channels.Count > 0;
            }
        }

        public IDisposable Register(IPresentationHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_handlers.ContainsKey(handler.RequestType))
                {
                    throw new InvalidOperationException(
                        $"A presentation handler for '{handler.RequestType.Name}' is already registered.");
                }

                _handlers.Add(handler.RequestType, handler);
            }

            return new HandlerRegistration(this, handler);
        }

        public PresentationHandle Publish(
            PresentationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            PresentationInstance instance;
            List<PresentationInstance> replaced = null;
            bool startNow = false;
            bool skip = false;
            bool completeImmediately = false;

            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_handlers.TryGetValue(request.GetType(), out IPresentationHandler handler))
                {
                    throw new InvalidOperationException(
                        $"No presentation handler is registered for '{request.GetType().Name}'.");
                }

                instance = new PresentationInstance(_nextId++, request, handler);
                instance.Handle.BindCancel(() => Cancel(instance));

                if (cancellationToken.IsCancellationRequested)
                {
                    instance.Complete(new PresentationOutcome(
                        PresentationStatus.Cancelled,
                        "Presentation was cancelled before scheduling."));
                    return instance.Handle;
                }

                if (_executionMode == PresentationExecutionMode.CompleteImmediately)
                {
                    completeImmediately = true;
                }
                else
                {
                    if (!_channels.TryGetValue(request.Channel, out ChannelState state))
                    {
                        state = new ChannelState();
                        _channels.Add(request.Channel, state);
                    }

                    bool busy = state.Current != null || state.Pending.Count > 0;
                    switch (request.ConflictPolicy)
                    {
                        case PresentationConflictPolicy.Queue:
                            if (state.Current == null)
                            {
                                state.Current = instance;
                                startNow = true;
                            }
                            else
                            {
                                instance.PendingNode = state.Pending.AddLast(instance);
                            }
                            break;

                        case PresentationConflictPolicy.ReplaceCurrent:
                            replaced = CollectAndClear(state);
                            state.Current = instance;
                            startNow = true;
                            break;

                        case PresentationConflictPolicy.IgnoreNew:
                            if (busy)
                            {
                                skip = true;
                            }
                            else
                            {
                                state.Current = instance;
                                startNow = true;
                            }
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            if (completeImmediately)
            {
                CompleteImmediately(instance);
                return instance.Handle;
            }

            instance.AttachExternalCancellation(cancellationToken, () => Cancel(instance));

            if (replaced != null)
            {
                foreach (PresentationInstance old in replaced)
                    CancelReplaced(old);
            }

            if (skip)
            {
                instance.Complete(new PresentationOutcome(
                    PresentationStatus.Skipped,
                    "The presentation channel is busy."));
            }
            else if (startNow)
            {
                RunAsync(instance).Forget();
            }

            return instance.Handle;
        }

        private static void CompleteImmediately(PresentationInstance instance)
        {
            PresentationOutcome outcome;
            try
            {
                instance.Handler.CompleteImmediately(instance.Request);
                outcome = new PresentationOutcome(
                    PresentationStatus.CompletedImmediately,
                    "Presentation was synchronously advanced to its final state.");
            }
            catch (Exception exception)
            {
                outcome = new PresentationOutcome(
                    PresentationStatus.Faulted,
                    exception.Message,
                    exception);
            }

            instance.Complete(outcome);
        }

        public void Dispose()
        {
            List<PresentationInstance> all = new();
            lock (_gate)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                foreach (ChannelState state in _channels.Values)
                    all.AddRange(CollectAndClear(state));
                _channels.Clear();
                _handlers.Clear();
            }

            foreach (PresentationInstance instance in all)
                CancelReplaced(instance, "Presentation dispatcher was disposed.");
        }

        private async UniTaskVoid RunAsync(PresentationInstance instance)
        {
            while (instance != null)
            {
                if (instance.Handle.IsFinished)
                    return;

                instance.Handle.MarkRunning();
                PresentationOutcome outcome;
                try
                {
                    instance.Cancellation.Token.ThrowIfCancellationRequested();
                    await instance.Handler.PresentAsync(instance.Request, instance.Cancellation.Token);

                    outcome = instance.Cancellation.IsCancellationRequested
                        ? new PresentationOutcome(PresentationStatus.Cancelled, "Presentation was cancelled.")
                        : new PresentationOutcome(PresentationStatus.Completed);
                }
                catch (OperationCanceledException) when (instance.Cancellation.IsCancellationRequested)
                {
                    outcome = new PresentationOutcome(PresentationStatus.Cancelled, "Presentation was cancelled.");
                }
                catch (Exception exception)
                {
                    outcome = new PresentationOutcome(PresentationStatus.Faulted, exception.Message, exception);
                }

                instance = FinishRunning(instance, outcome);
            }
        }

        private void Cancel(PresentationInstance instance)
        {
            bool completePending = false;
            bool cancelRunning = false;
            lock (_gate)
            {
                if (instance.Handle.IsFinished)
                    return;

                if (_channels.TryGetValue(instance.Request.Channel, out ChannelState state) &&
                    instance.PendingNode != null)
                {
                    state.Pending.Remove(instance.PendingNode);
                    instance.PendingNode = null;
                    completePending = true;
                }
                else
                {
                    cancelRunning = true;
                }
            }

            if (completePending)
            {
                instance.Complete(new PresentationOutcome(
                    PresentationStatus.Cancelled,
                    "Pending presentation was cancelled."));
            }
            else if (cancelRunning)
            {
                // CancellationToken 回调可能同步恢复 Handler；不要在调度器锁内执行。
                instance.TryCancel();
            }
        }

        private PresentationInstance FinishRunning(
            PresentationInstance instance,
            PresentationOutcome outcome)
        {
            PresentationInstance next = null;
            lock (_gate)
            {
                if (_channels.TryGetValue(instance.Request.Channel, out ChannelState state) &&
                    ReferenceEquals(state.Current, instance))
                {
                    state.Current = null;
                    while (state.Pending.Count > 0 && next == null)
                    {
                        next = state.Pending.First.Value;
                        state.Pending.RemoveFirst();
                        next.PendingNode = null;
                        if (next.Handle.IsFinished)
                            next = null;
                    }

                    if (next == null)
                        _channels.Remove(instance.Request.Channel);
                    else
                        state.Current = next;
                }
            }

            // 先提交通道状态，再唤醒等待者。这样等待者在 continuation 中立即 Publish
            // 时，不会观察到已经结束的实例仍占用通道。
            instance.Complete(outcome);
            return next;
        }

        private static List<PresentationInstance> CollectAndClear(ChannelState state)
        {
            var result = new List<PresentationInstance>(state.Pending.Count + 1);
            if (state.Current != null)
                result.Add(state.Current);

            foreach (PresentationInstance pending in state.Pending)
            {
                pending.PendingNode = null;
                result.Add(pending);
            }

            state.Current = null;
            state.Pending.Clear();
            return result;
        }

        private static void CancelReplaced(
            PresentationInstance instance,
            string reason = "Presentation was replaced by a newer request.")
        {
            if (instance.Started)
            {
                instance.TryCancel();
                return;
            }

            instance.Complete(new PresentationOutcome(
                PresentationStatus.Cancelled,
                reason));
        }

        private void Unregister(IPresentationHandler handler)
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(handler.RequestType, out IPresentationHandler current) &&
                    ReferenceEquals(current, handler))
                {
                    _handlers.Remove(handler.RequestType);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PresentationDispatcher));
        }

        private sealed class ChannelState
        {
            public PresentationInstance Current;
            public LinkedList<PresentationInstance> Pending { get; } = new();
        }

        private sealed class PresentationInstance
        {
            private CancellationTokenRegistration _externalCancellation;
            private int _isCompleted;

            public PresentationInstance(
                long id,
                PresentationRequest request,
                IPresentationHandler handler)
            {
                Request = request;
                Handler = handler;
                Handle = new PresentationHandle(id, request);
                Cancellation = new CancellationTokenSource();
            }

            public PresentationRequest Request { get; }
            public IPresentationHandler Handler { get; }
            public PresentationHandle Handle { get; }
            public CancellationTokenSource Cancellation { get; }
            public LinkedListNode<PresentationInstance> PendingNode { get; set; }
            public bool Started => Handle.Status == PresentationStatus.Running;

            public void AttachExternalCancellation(
                CancellationToken cancellationToken,
                Action cancel)
            {
                if (!cancellationToken.CanBeCanceled || Handle.IsFinished)
                    return;

                _externalCancellation = cancellationToken.Register(cancel);
                if (Handle.IsFinished)
                    _externalCancellation.Dispose();
            }

            public void Complete(PresentationOutcome outcome)
            {
                if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
                    return;

                Handle.Complete(outcome);
                _externalCancellation.Dispose();
                Cancellation.Dispose();
            }

            public void TryCancel()
            {
                if (Volatile.Read(ref _isCompleted) != 0)
                    return;

                try
                {
                    Cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Complete 与取消可能分别来自 Handler 和外部 CancellationToken；
                    // 完成已胜出时，取消无需再做任何事。
                }
                catch (AggregateException exception)
                {
                    UniTask.FromException(exception).Forget();
                }
            }
        }

        private sealed class HandlerRegistration : IDisposable
        {
            private PresentationDispatcher _owner;
            private readonly IPresentationHandler _handler;

            public HandlerRegistration(
                PresentationDispatcher owner,
                IPresentationHandler handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                PresentationDispatcher owner = Interlocked.Exchange(ref _owner, null);
                owner?.Unregister(_handler);
            }
        }
    }
}
