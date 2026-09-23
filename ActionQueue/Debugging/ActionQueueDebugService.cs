using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue
{
    /// <summary>
    /// 独立维护 ActionQueue 的调试状态、树模型和单步许可。
    /// 队列只通过生命周期通知写入这里，不持有任何可视化模型。
    /// </summary>
    public sealed class ActionQueueDebugService
    {
        private readonly Dictionary<long, ActionQueueDebugNode> _actions = new();
        private readonly Dictionary<long, ActionQueueDebugNode> _reactors = new();
        private readonly List<ActionQueueDebugNode> _activeRoots = new();
        private readonly List<string> _activeScopedReactors = new();

        private List<ActionQueueDebugNode> _lastCompletedRoots = new();
        private List<string> _lastCompletedScopedReactors = new();
        private long _nextReactorNodeId = -1;
        private long _activeChainId;
        private long _lastCompletedChainId;
        private int _lastCompletedActionCount;
        private bool _hasActiveChain;

        private bool _breakpointMode;
        private bool _isPaused;
        private bool _stepPermit;
        private string _pausedNode = string.Empty;
        private string _currentNode = string.Empty;
        private UniTaskCompletionSource<bool> _waiter;
        private int _recordingLeaseCount;
        private long _version;

        public bool BreakpointMode => _breakpointMode;
        public bool IsPaused => _isPaused;
        public bool IsRecording => _recordingLeaseCount > 0 || _breakpointMode;
        public long Version => _version;

        internal bool ObservesWorkItems => IsRecording;

        public event Action StateChanged;

        /// <summary>
        /// 请求记录调试树。最后一个租约释放后会停止记录并释放历史数据。
        /// </summary>
        public IDisposable AcquireRecording()
        {
            bool wasRecording = IsRecording;
            _recordingLeaseCount++;
            if (!wasRecording)
            {
                ClearRecordedData();
                NotifyChanged();
            }

            return new RecordingLease(this);
        }

        public void SetBreakpointMode(bool enabled)
        {
            if (_breakpointMode == enabled)
                return;

            bool wasRecording = IsRecording;
            _breakpointMode = enabled;
            _stepPermit = false;
            if (!enabled)
                ReleaseWaiter();

            if (!wasRecording && IsRecording)
                ClearRecordedData();
            else if (wasRecording && !IsRecording)
                ClearRecordedData();

            NotifyChanged();
        }

        public void ContinueOneNode()
        {
            if (!_breakpointMode)
                return;

            if (_waiter != null)
                ReleaseWaiter();
            else
                _stepPermit = true;

            NotifyChanged();
        }

        internal void BeginChain(
            long chainId,
            IReadOnlyList<IGameActionReactor> chainReactors)
        {
            if (!IsRecording)
                return;

            ClearActiveChain();
            _hasActiveChain = true;
            _activeChainId = chainId;

            if (chainReactors != null)
            {
                foreach (IGameActionReactor reactor in chainReactors)
                {
                    if (reactor != null)
                    {
                        _activeScopedReactors.Add(
                            $"Chain:{chainId} | {reactor.DebugName} | {reactor.Timing}");
                    }
                }
            }

            NotifyChanged();
        }

        internal void RegisterAction(
            long id,
            long parentId,
            GameAction action,
            string cause)
        {
            if (!IsRecording)
                return;

            var node = new ActionQueueDebugNode(
                id,
                parentId,
                ActionQueueDebugNodeKind.Action,
                action.DebugName,
                cause,
                action.ExecutionKind,
                action.OpenReactionPhases);

            _actions.Add(id, node);
            if (parentId != 0 && _actions.TryGetValue(parentId, out ActionQueueDebugNode parent))
                parent.Children.Add(node);
            else
                _activeRoots.Add(node);

            foreach (IGameActionReactor reactor in action.LocalReactors)
            {
                _activeScopedReactors.Add(
                    $"Action:{action.DebugName}#{id} | {reactor.DebugName} | {reactor.Timing}");
            }
        }

        internal long RegisterReactor(
            long ownerActionId,
            ReactorRegistry.ReactorInvocation invocation)
        {
            if (!IsRecording)
                return 0;

            long id = _nextReactorNodeId--;
            var node = new ActionQueueDebugNode(
                id,
                ownerActionId,
                ActionQueueDebugNodeKind.Reactor,
                invocation.Reactor.DebugName,
                $"{invocation.Reactor.Timing} | {invocation.Context.Relation} | " +
                $"Entity {invocation.Context.MatchedEntity?.ReactorName ?? "-"} | " +
                $"TargetIndex {invocation.Context.TargetIndex} | " +
                $"Priority {invocation.Reactor.Priority}");

            _reactors.Add(id, node);
            if (_actions.TryGetValue(ownerActionId, out ActionQueueDebugNode owner))
                owner.Reactors.Add(node);

            return id;
        }

        internal void SetActionExecuting(long actionId, string phase, string cause)
        {
            if (!IsRecording)
                return;

            if (!_actions.TryGetValue(actionId, out ActionQueueDebugNode node))
                return;

            node.State = ActionQueueDebugNodeState.Executing;
            node.Detail = $"{phase} | Cause: {cause}";
        }

        internal void SetActionOutcome(long actionId, ActionOutcome outcome, string cause)
        {
            if (!IsRecording)
                return;

            if (!_actions.TryGetValue(actionId, out ActionQueueDebugNode node))
                return;

            node.Outcome = outcome.ToString();
            node.Detail = $"AfterResolved | Cause: {cause}";
        }

        internal void SetActionResolved(long actionId)
        {
            if (!IsRecording)
                return;

            if (_actions.TryGetValue(actionId, out ActionQueueDebugNode node))
                node.State = ActionQueueDebugNodeState.Resolved;
        }

        internal void SetReactorState(long reactorNodeId, ActionQueueDebugNodeState state)
        {
            if (!IsRecording)
                return;

            if (_reactors.TryGetValue(reactorNodeId, out ActionQueueDebugNode node))
                node.State = state;
        }

        internal void MarkPendingSkipped()
        {
            if (!IsRecording)
                return;

            var pending = new Stack<ActionQueueDebugNode>(_activeRoots);
            while (pending.Count > 0)
            {
                ActionQueueDebugNode node = pending.Pop();
                if (node.State != ActionQueueDebugNodeState.Resolved)
                    node.State = ActionQueueDebugNodeState.Skipped;

                foreach (ActionQueueDebugNode reactor in node.Reactors)
                    pending.Push(reactor);

                foreach (ActionQueueDebugNode child in node.Children)
                    pending.Push(child);
            }
        }

        internal async UniTask BeforeWorkItemAsync(
            string nodeName,
            bool isBreakpointNode,
            CancellationToken cancellationToken)
        {
            if (!IsRecording)
                return;

            if (isBreakpointNode)
                await WaitForPermitAsync(nodeName, cancellationToken);

            _currentNode = nodeName;
            NotifyChanged();
        }

        internal void CompleteChain(int executedActionCount)
        {
            if (!IsRecording)
                return;

            _lastCompletedRoots = new List<ActionQueueDebugNode>(_activeRoots);
            _lastCompletedScopedReactors = new List<string>(_activeScopedReactors);
            _lastCompletedChainId = _activeChainId;
            _lastCompletedActionCount = executedActionCount;
            ClearActiveChain();
            NotifyChanged();
        }

        internal ActionQueueDebugSnapshot CreateSnapshot(
            int maxActionsPerChain,
            IEnumerable<string> pendingRoots,
            IEnumerable<string> pendingWorkItems,
            IEnumerable<string> registeredReactors,
            int activeActionCount)
        {
            var snapshot = new ActionQueueDebugSnapshot
            {
                BreakpointMode = _breakpointMode,
                IsPaused = _isPaused,
                PausedNode = _pausedNode,
                CurrentNode = _currentNode,
                MaxActionsPerChain = maxActionsPerChain
            };

            snapshot.PendingRoots.AddRange(pendingRoots);
            snapshot.PendingWorkItems.AddRange(pendingWorkItems);
            snapshot.RegisteredReactors.AddRange(registeredReactors);

            if (!IsRecording)
                return snapshot;

            if (_hasActiveChain)
            {
                snapshot.HasChain = true;
                snapshot.ChainId = _activeChainId;
                snapshot.ExecutedActionCount = activeActionCount;
                snapshot.Roots.AddRange(_activeRoots);
                snapshot.RegisteredReactors.AddRange(_activeScopedReactors);
            }
            else if (_lastCompletedRoots.Count > 0)
            {
                snapshot.HasChain = true;
                snapshot.IsLastCompletedChain = true;
                snapshot.ChainId = _lastCompletedChainId;
                snapshot.ExecutedActionCount = _lastCompletedActionCount;
                snapshot.Roots.AddRange(_lastCompletedRoots);
                snapshot.RegisteredReactors.AddRange(_lastCompletedScopedReactors);
            }

            return snapshot;
        }

        internal void NotifyChanged()
        {
            if (!IsRecording)
                return;

            _version++;
            Action changed = StateChanged;
            if (changed == null)
                return;

            foreach (Delegate subscriber in changed.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception exception)
                {
                    UniTask.FromException(exception).Forget();
                }
            }
        }

        internal void Stop()
        {
            if (!IsRecording)
                return;

            ClearActiveChain();
            _currentNode = string.Empty;
            ReleaseWaiter();
            NotifyChanged();
        }

        internal void ClearAll()
        {
            ClearRecordedData();
            _stepPermit = false;
            ReleaseWaiter();
            NotifyChanged();
        }

        internal void Dispose()
        {
            _breakpointMode = false;
            _recordingLeaseCount = 0;
            _stepPermit = false;
            ClearRecordedData();
            ReleaseWaiter();
            NotifyChanged();
            StateChanged = null;
        }

        private void ReleaseRecording()
        {
            if (_recordingLeaseCount == 0)
                return;

            _recordingLeaseCount--;
            if (_recordingLeaseCount > 0)
                return;

            if (_breakpointMode)
            {
                _breakpointMode = false;
                _stepPermit = false;
                ReleaseWaiter();
            }

            ClearRecordedData();
            NotifyChanged();
        }

        private async UniTask WaitForPermitAsync(
            string nodeName,
            CancellationToken cancellationToken)
        {
            if (!_breakpointMode)
                return;

            if (_stepPermit)
            {
                _stepPermit = false;
                return;
            }

            _isPaused = true;
            _pausedNode = nodeName;
            var waiter = new UniTaskCompletionSource<bool>();
            _waiter = waiter;
            NotifyChanged();

            try
            {
                await waiter.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                if (ReferenceEquals(_waiter, waiter))
                    _waiter = null;

                _isPaused = false;
                _pausedNode = string.Empty;
                NotifyChanged();
            }
        }

        private void ReleaseWaiter()
        {
            UniTaskCompletionSource<bool> waiter = _waiter;
            _waiter = null;
            _isPaused = false;
            _pausedNode = string.Empty;
            waiter?.TrySetResult(true);
        }

        private void ClearActiveChain()
        {
            _hasActiveChain = false;
            _activeChainId = 0;
            _actions.Clear();
            _reactors.Clear();
            _activeRoots.Clear();
            _activeScopedReactors.Clear();
            _currentNode = string.Empty;
        }

        private void ClearRecordedData()
        {
            ClearActiveChain();
            _lastCompletedRoots.Clear();
            _lastCompletedScopedReactors.Clear();
            _lastCompletedChainId = 0;
            _lastCompletedActionCount = 0;
        }

        private sealed class RecordingLease : IDisposable
        {
            private ActionQueueDebugService _owner;

            public RecordingLease(ActionQueueDebugService owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                ActionQueueDebugService owner = _owner;
                _owner = null;
                owner?.ReleaseRecording();
            }
        }
    }
}
