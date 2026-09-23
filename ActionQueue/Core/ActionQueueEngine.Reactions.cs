using System;
using System.Collections.Generic;

namespace CardGame.ActionQueue
{
    public sealed partial class ActionQueueEngine
    {
        #region Reaction Processing

        private void BeginReactionBatch(
            GameAction action,
            ReactionTiming timing,
            ActionOutcome? outcome,
            ActionRuntime owner,
            Action<ReactionResponse> onCompleted)
        {
            List<ReactorRegistry.ReactorInvocation> invocations = Reactors.Collect(
                action,
                timing,
                outcome,
                _activeChain.Id,
                owner.Id,
                _activeChain.Request.ChainReactors,
                owner.ScopedReactors,
                ReactionGates);

            var state = new ReactionBatchState(owner, timing, invocations, onCompleted);
            DebugRegisterReactors(state);
            AddWorkItem(QueueWorkItem.ForReactionAdvance(state), true);
            DebugNotifyChanged();
        }

        private void AdvanceReactionBatch(ReactionBatchState state)
        {
            if (state.Response.StopPropagation)
            {
                DebugSkipReactors(state, state.NextIndex);
                state.NextIndex = state.Invocations.Count;
            }

            if (state.NextIndex >= state.Invocations.Count)
            {
                state.OnCompleted(state.Response);
                DebugNotifyChanged();
                return;
            }

            int index = state.NextIndex++;
            AddWorkItem(QueueWorkItem.ForReactor(state, index), true);
        }

        private void RunReactor(ReactionBatchState state, int index)
        {
            DebugSetReactorExecuting(state, index);
            state.Invocations[index].Invoke(state.Response);
            DebugSetReactorResolved(state, index);
            AddWorkItem(QueueWorkItem.ForReactionAdvance(state), true);
            DebugNotifyChanged();
        }

        private void ScheduleReactionActions(ReactionResponse response, ActionRuntime owner)
        {
            IReadOnlyList<ReactionActionRequest> actions = response.Actions;

            // Bottom 保持声明顺序。
            for (int i = 0; i < actions.Count; i++)
            {
                ReactionActionRequest request = actions[i];
                if (request.Position == ReactionQueuePosition.Bottom)
                {
                    ScheduleAction(
                        request.Action,
                        false,
                        owner.Id,
                        FormatReactionCause(owner, request),
                        null);
                }
            }

            // AddFirst 需要逆序插入，确保声明的 A、B 仍按 A、B 执行。
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                ReactionActionRequest request = actions[i];
                if (request.Position == ReactionQueuePosition.Immediate)
                {
                    ScheduleAction(
                        request.Action,
                        true,
                        owner.Id,
                        FormatReactionCause(owner, request),
                        null);
                }
            }
        }

        private static string FormatReactionCause(
            ActionRuntime owner,
            ReactionActionRequest request)
        {
            return string.IsNullOrEmpty(request.Cause)
                ? $"Reaction to {owner.Action.DebugName}"
                : request.Cause;
        }

        #endregion
    }
}
