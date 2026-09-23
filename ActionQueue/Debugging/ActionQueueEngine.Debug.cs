using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue
{
    public sealed partial class ActionQueueEngine
    {
        #region Debug Integration

        private ActionQueueDebugService _debugger;
        private ReactorRegistry _observedReactorRegistry;
        private bool _isReactorRegistryObserved;

        public ActionQueueDebugService Debugger
        {
            get
            {
                ThrowIfDisposed();

                if (_debugger == null)
                {
                    _debugger = new ActionQueueDebugService();
                    if (_observedReactorRegistry != null)
                        DebugAttachReactorRegistry(_observedReactorRegistry);
                }

                return _debugger;
            }
        }

        private void DebugBeginChain(
            long chainId,
            IReadOnlyList<IGameActionReactor> chainReactors)
        {
            _debugger?.BeginChain(chainId, chainReactors);
        }

        private UniTask DebugBeforeWorkItemAsync(
            QueueWorkItem workItem,
            CancellationToken cancellationToken)
        {
            if (_debugger == null || !_debugger.ObservesWorkItems)
                return UniTask.CompletedTask;

            return _debugger.BeforeWorkItemAsync(
                workItem.DebugName,
                workItem.IsBreakpointNode,
                cancellationToken);
        }

        private void DebugRegisterAction(ActionRuntime runtime)
        {
            _debugger?.RegisterAction(
                runtime.Id,
                runtime.ParentId,
                runtime.Action,
                runtime.Cause);
        }

        private void DebugSetActionExecuting(ActionRuntime runtime, string phase)
        {
            _debugger?.SetActionExecuting(runtime.Id, phase, runtime.Cause);
        }

        private void DebugSetActionOutcome(ActionRuntime runtime, ActionOutcome outcome)
        {
            _debugger?.SetActionOutcome(runtime.Id, outcome, runtime.Cause);
        }

        private void DebugSetActionResolved(ActionRuntime runtime)
        {
            _debugger?.SetActionResolved(runtime.Id);
        }

        private void DebugRegisterReactors(ReactionBatchState state)
        {
            if (_debugger == null || !_debugger.IsRecording)
                return;

            state.ReactorNodeIds = new List<long>(state.Invocations.Count);
            foreach (ReactorRegistry.ReactorInvocation invocation in state.Invocations)
            {
                long id = _debugger.RegisterReactor(state.Owner.Id, invocation);
                if (id != 0)
                    state.ReactorNodeIds.Add(id);
            }
        }

        private void DebugSkipReactors(ReactionBatchState state, int startIndex)
        {
            if (state.ReactorNodeIds == null)
                return;

            for (int i = startIndex; i < state.ReactorNodeIds.Count; i++)
            {
                _debugger.SetReactorState(
                    state.ReactorNodeIds[i],
                    ActionQueueDebugNodeState.Skipped);
            }
        }

        private void DebugSetReactorExecuting(ReactionBatchState state, int index)
        {
            if (state.ReactorNodeIds == null || index >= state.ReactorNodeIds.Count)
                return;

            _debugger.SetReactorState(
                state.ReactorNodeIds[index],
                ActionQueueDebugNodeState.Executing);
        }

        private void DebugSetReactorResolved(ReactionBatchState state, int index)
        {
            if (state.ReactorNodeIds == null || index >= state.ReactorNodeIds.Count)
                return;

            _debugger.SetReactorState(
                state.ReactorNodeIds[index],
                ActionQueueDebugNodeState.Resolved);
        }

        private void DebugMarkPendingSkipped()
        {
            _debugger?.MarkPendingSkipped();
        }

        private void DebugCompleteChain(int executedActionCount)
        {
            _debugger?.CompleteChain(executedActionCount);
        }

        private void DebugNotifyChanged()
        {
            _debugger?.NotifyChanged();
        }

        private void DebugStop()
        {
            _debugger?.Stop();
        }

        private void DebugClearAll()
        {
            // 清空可视化历史，并释放可能存在的断点等待器。
            _debugger?.ClearAll();
        }

        private void DebugDispose()
        {
            if (_isReactorRegistryObserved && _observedReactorRegistry != null)
                _observedReactorRegistry.Changed -= DebugNotifyChanged;

            _isReactorRegistryObserved = false;
            _debugger?.Dispose();
            _debugger = null;
        }

        private void DebugAttachReactorRegistry(ReactorRegistry registry)
        {
            if (ReferenceEquals(_observedReactorRegistry, registry) &&
                (_debugger == null || _isReactorRegistryObserved))
                return;

            if (_isReactorRegistryObserved && _observedReactorRegistry != null)
                _observedReactorRegistry.Changed -= DebugNotifyChanged;

            _observedReactorRegistry = registry;
            _isReactorRegistryObserved = false;
            if (_debugger != null && _observedReactorRegistry != null)
            {
                _observedReactorRegistry.Changed += DebugNotifyChanged;
                _isReactorRegistryObserved = true;
            }
        }

        public ActionQueueDebugSnapshot GetDebugSnapshot()
        {
            ThrowIfDisposed();

            var pendingRoots = new List<string>(_rootQueue.Count);
            foreach (RootRequest request in _rootQueue)
                pendingRoots.Add(request.RootAction.DebugName);

            var pendingWorkItems = new List<string>(_workQueue.Count);
            for (int i = 0; i < _workQueue.Count; i++)
                pendingWorkItems.Add(_workQueue[i].DebugName);

            int activeActionCount = _activeChain?.ExecutedActionCount ?? 0;
            return Debugger.CreateSnapshot(
                MaxActionsPerChain,
                pendingRoots,
                pendingWorkItems,
                Reactors.GetDebugRegistrationDescriptions(),
                activeActionCount);
        }

        #endregion
    }
}
