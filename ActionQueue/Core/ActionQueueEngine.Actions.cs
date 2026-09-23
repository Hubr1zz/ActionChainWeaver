using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue
{
    public sealed partial class ActionQueueEngine
    {
        #region Action Processing

        private void ScheduleAction(
            GameAction action,
            bool immediate,
            long parentActionId,
            string cause,
            Action<ActionOutcome> onCompleted)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            EngineGuards.ValidateBeforeSchedule(action);

            if (!action.TryMarkScheduled())
            {
                throw new InvalidOperationException(
                    $"Action instance '{action.DebugName}' was scheduled more than once. " +
                    "GameAction instances are single-use; create a new instance for every queue entry.");
            }

            IReadOnlyList<IGameActionReactor> inherited = Array.Empty<IGameActionReactor>();
            if (parentActionId != 0)
            {
                if (!_activeChain.TryGetRuntime(parentActionId, out ActionRuntime parent))
                    throw new InvalidOperationException($"Parent ActionRuntime '{parentActionId}' was not found.");
                inherited = parent.DescendantReactors;
            }

            BuildScopedReactors(
                inherited,
                action.ScopedReactors,
                out IReadOnlyList<IGameActionReactor> currentScope,
                out IReadOnlyList<IGameActionReactor> descendantScope);

            var runtime = new ActionRuntime(
                _nextActionId++,
                parentActionId,
                action,
                cause,
                onCompleted,
                currentScope,
                descendantScope);

            _activeChain.AddRuntime(runtime);

            DebugRegisterAction(runtime);
            AddWorkItem(QueueWorkItem.ForAction(runtime, ActionWorkPhase.Before), immediate);
            DebugNotifyChanged();
        }

        private static void BuildScopedReactors(
            IReadOnlyList<IGameActionReactor> inherited,
            IReadOnlyList<ScopedReactorBinding> declared,
            out IReadOnlyList<IGameActionReactor> current,
            out IReadOnlyList<IGameActionReactor> descendants)
        {
            if ((declared == null || declared.Count == 0))
            {
                current = inherited;
                descendants = inherited;
                return;
            }

            var currentList = new System.Collections.Generic.List<IGameActionReactor>(
                inherited.Count + declared.Count);
            var descendantList = new System.Collections.Generic.List<IGameActionReactor>(
                inherited.Count + declared.Count);
            for (int i = 0; i < inherited.Count; i++)
            {
                currentList.Add(inherited[i]);
                descendantList.Add(inherited[i]);
            }

            for (int i = 0; i < declared.Count; i++)
            {
                ScopedReactorBinding binding = declared[i];
                if (binding.IncludeOwner)
                    currentList.Add(binding.Reactor);
                descendantList.Add(binding.Reactor);
            }

            current = currentList;
            descendants = descendantList;
        }

        private UniTask ProcessBeforeAsync(ActionRuntime runtime)
        {
            if (!TryEnterAction(runtime))
                return UniTask.CompletedTask;

            if ((runtime.Action.OpenReactionPhases & ReactionPhases.BeforeExecution) == 0)
            {
                AddWorkItem(QueueWorkItem.ForAction(runtime, ActionWorkPhase.Execute), true);
                return UniTask.CompletedTask;
            }

            DebugSetActionExecuting(runtime, "BeforeExecution");
            BeginReactionBatch(
                runtime.Action,
                ReactionTiming.BeforeExecution,
                null,
                runtime,
                response => FinishBefore(runtime, response));

            DebugNotifyChanged();
            return UniTask.CompletedTask;
        }

        private void FinishBefore(ActionRuntime runtime, ReactionResponse response)
        {
            if (response.Prevention.HasValue)
            {
                AddWorkItem(QueueWorkItem.ForResolve(runtime, response.Prevention.Value), true);
                ScheduleReactionActions(response, runtime);
                return;
            }

            AddWorkItem(QueueWorkItem.ForAction(runtime, ActionWorkPhase.Execute), true);
            ScheduleReactionActions(response, runtime);
        }

        private async UniTask ProcessExecuteAsync(
            ActionRuntime runtime,
            CancellationToken cancellationToken)
        {
            if (runtime.Action.TryGetPrevention(out ActionOutcome prevention))
            {
                ResolveRuntime(runtime, prevention);
                return;
            }

            if (runtime.Action is CompositeGameAction composite)
            {
                var state = new CompositeState(runtime, composite);
                AddWorkItem(QueueWorkItem.ForCompositeAdvance(state), true);
                return;
            }

            var context = new ActionExecutionContext(
                this,
                _activeChain.Id,
                runtime.Id,
                SkipPresentationWaits);
            DebugSetActionExecuting(runtime, "Execute");
            ActionOutcome outcome = await runtime.Action.ExecuteInternalAsync(context, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                AbortActiveChain(ActionOutcome.Cancelled("Action execution was cancelled."));
                return;
            }

            ResolveRuntime(runtime, outcome);
        }

        private void AdvanceComposite(CompositeState state)
        {
            if (state.Runtime.Action.TryGetPrevention(out ActionOutcome prevention))
            {
                ResolveRuntime(state.Runtime, prevention);
                return;
            }

            GameAction child = state.Composite.GetNextChildInternal(state.Context);
            if (child == null)
            {
                ResolveRuntime(state.Runtime, state.Composite.ResolveInternal(state.Context));
                return;
            }

            ScheduleAction(
                child,
                true,
                state.Runtime.Id,
                state.Runtime.Action.DebugName,
                outcome =>
                {
                    state.Context.AddOutcome(outcome);
                    AddWorkItem(QueueWorkItem.ForCompositeAdvance(state), true);
                });
        }

        private void ResolveRuntime(ActionRuntime runtime, ActionOutcome outcome)
        {
            if (runtime.IsResolved)
                throw new InvalidOperationException($"Action {runtime.Id} was resolved more than once.");

            runtime.IsResolved = true;
            runtime.Outcome = outcome;
            DebugSetActionOutcome(runtime, outcome);
            if (LogLevel >= ActionQueueLogLevel.Verbose && Logger != null)
                LogVerbose($"[ActionQueue:{_activeChain.Id}] {runtime.Action.DebugName} => {outcome}");

            if ((runtime.Action.OpenReactionPhases & ReactionPhases.AfterResolved) == 0)
            {
                FinishResolved(runtime, new ReactionResponse());
                return;
            }

            BeginReactionBatch(
                runtime.Action,
                ReactionTiming.AfterResolved,
                outcome,
                runtime,
                response => FinishResolved(runtime, response));
        }

        private void FinishResolved(ActionRuntime runtime, ReactionResponse response)
        {
            DebugSetActionResolved(runtime);
            // Composite continuation 先入队，随后 AddToTop 的 Reaction Action 会排到它前面。
            runtime.OnCompleted?.Invoke(runtime.Outcome);
            ScheduleReactionActions(response, runtime);
            DebugNotifyChanged();
        }

        private bool TryEnterAction(ActionRuntime runtime)
        {
            _activeChain.ExecutedActionCount++;
            _activeChain.AddTrace(runtime.Action.DebugName, runtime.Cause);

            if (_activeChain.ExecutedActionCount <= MaxActionsPerChain)
                return true;

            var message = new StringBuilder();
            message.Append("[ActionQueue] Chain ")
                .Append(_activeChain.Id)
                .Append(" exceeded MaxActionsPerChain (")
                .Append(MaxActionsPerChain)
                .AppendLine("). Possible indirect reaction loop.")
                .AppendLine("Recent action trace:")
                .Append(_activeChain.FormatTrace());

            LogWarning(message.ToString());
            AbortActiveChain(ActionOutcome.Failure(
                $"Loop guard exceeded {MaxActionsPerChain} actions."));
            return false;
        }

        private void AbortActiveChain(ActionOutcome outcome)
        {
            if (_activeChain == null || _activeChain.IsAborted)
                return;

            _activeChain.IsAborted = true;
            _activeChain.AbortOutcome = outcome;
            DebugMarkPendingSkipped();
            _workQueue.Clear();
            DebugNotifyChanged();
        }

        #endregion
    }
}
