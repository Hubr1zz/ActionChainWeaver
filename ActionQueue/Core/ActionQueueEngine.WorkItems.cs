using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue
{
    public sealed partial class ActionQueueEngine
    {
        #region Work Item State Machine

        private enum ActionWorkPhase
        {
            Before,
            Execute
        }

        private enum WorkItemKind
        {
            Action,
            CompositeAdvance,
            ResolveAction,
            ReactionAdvance,
            Reactor
        }

        /// <summary>
        /// 以 tagged struct 表示队列工作项，避免“工作项对象 + LinkedListNode”的双重分配。
        /// </summary>
        private readonly struct QueueWorkItem
        {
            private readonly WorkItemKind _kind;
            private readonly ActionRuntime _runtime;
            private readonly CompositeState _compositeState;
            private readonly ReactionBatchState _reactionState;
            private readonly ActionWorkPhase _phase;
            private readonly ActionOutcome _outcome;
            private readonly int _index;

            private QueueWorkItem(
                WorkItemKind kind,
                ActionRuntime runtime = null,
                CompositeState compositeState = null,
                ReactionBatchState reactionState = null,
                ActionWorkPhase phase = default,
                ActionOutcome outcome = default,
                int index = 0)
            {
                _kind = kind;
                _runtime = runtime;
                _compositeState = compositeState;
                _reactionState = reactionState;
                _phase = phase;
                _outcome = outcome;
                _index = index;
            }

            public bool IsBreakpointNode =>
                _kind == WorkItemKind.Action || _kind == WorkItemKind.Reactor;

            public string DebugName => _kind switch
            {
                WorkItemKind.Action => $"{_runtime.Action.DebugName}.{_phase}",
                WorkItemKind.CompositeAdvance =>
                    $"{_compositeState.Runtime.Action.DebugName}.Continuation",
                WorkItemKind.ResolveAction => $"{_runtime.Action.DebugName}.Resolve",
                WorkItemKind.ReactionAdvance =>
                    $"{_reactionState.Owner.Action.DebugName}." +
                    $"{_reactionState.Timing}.ReactionContinuation",
                WorkItemKind.Reactor =>
                    $"Reactor:{_reactionState.Invocations[_index].Reactor.DebugName} " +
                    $"({_reactionState.Timing} {_reactionState.Owner.Action.DebugName})",
                _ => _kind.ToString()
            };

            public static QueueWorkItem ForAction(ActionRuntime runtime, ActionWorkPhase phase) =>
                new(WorkItemKind.Action, runtime: runtime, phase: phase);

            public static QueueWorkItem ForCompositeAdvance(CompositeState state) =>
                new(WorkItemKind.CompositeAdvance, compositeState: state);

            public static QueueWorkItem ForResolve(ActionRuntime runtime, ActionOutcome outcome) =>
                new(WorkItemKind.ResolveAction, runtime: runtime, outcome: outcome);

            public static QueueWorkItem ForReactionAdvance(ReactionBatchState state) =>
                new(WorkItemKind.ReactionAdvance, reactionState: state);

            public static QueueWorkItem ForReactor(ReactionBatchState state, int index) =>
                new(WorkItemKind.Reactor, reactionState: state, index: index);

            public UniTask RunAsync(
                ActionQueueEngine engine,
                CancellationToken cancellationToken)
            {
                switch (_kind)
                {
                    case WorkItemKind.Action:
                        return _phase == ActionWorkPhase.Before
                            ? engine.ProcessBeforeAsync(_runtime)
                            : engine.ProcessExecuteAsync(_runtime, cancellationToken);
                    case WorkItemKind.CompositeAdvance:
                        engine.AdvanceComposite(_compositeState);
                        return UniTask.CompletedTask;
                    case WorkItemKind.ResolveAction:
                        engine.ResolveRuntime(_runtime, _outcome);
                        return UniTask.CompletedTask;
                    case WorkItemKind.ReactionAdvance:
                        engine.AdvanceReactionBatch(_reactionState);
                        return UniTask.CompletedTask;
                    case WorkItemKind.Reactor:
                        engine.RunReactor(_reactionState, _index);
                        return UniTask.CompletedTask;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private sealed class ActionRuntime
        {
            public ActionRuntime(
                long id,
                long parentId,
                GameAction action,
                string cause,
                Action<ActionOutcome> onCompleted,
                IReadOnlyList<IGameActionReactor> scopedReactors,
                IReadOnlyList<IGameActionReactor> descendantReactors)
            {
                Id = id;
                ParentId = parentId;
                Action = action;
                Cause = cause ?? string.Empty;
                OnCompleted = onCompleted;
                ScopedReactors = scopedReactors;
                DescendantReactors = descendantReactors;
            }

            public long Id { get; }
            public long ParentId { get; }
            public GameAction Action { get; }
            public string Cause { get; }
            public Action<ActionOutcome> OnCompleted { get; }
            public IReadOnlyList<IGameActionReactor> ScopedReactors { get; }
            public IReadOnlyList<IGameActionReactor> DescendantReactors { get; }
            public bool IsResolved { get; set; }
            public ActionOutcome Outcome { get; set; }
        }

        private sealed class CompositeState
        {
            public CompositeState(ActionRuntime runtime, CompositeGameAction composite)
            {
                Runtime = runtime;
                Composite = composite;
                Context = new CompositeExecutionContext();
            }

            public ActionRuntime Runtime { get; }
            public CompositeGameAction Composite { get; }
            public CompositeExecutionContext Context { get; }
        }

        private sealed class ReactionBatchState
        {
            public ReactionBatchState(
                ActionRuntime owner,
                ReactionTiming timing,
                List<ReactorRegistry.ReactorInvocation> invocations,
                Action<ReactionResponse> onCompleted)
            {
                Owner = owner;
                Timing = timing;
                Invocations = invocations;
                OnCompleted = onCompleted;
            }

            public ActionRuntime Owner { get; }
            public ReactionTiming Timing { get; }
            public List<ReactorRegistry.ReactorInvocation> Invocations { get; }
            public List<long> ReactorNodeIds { get; set; }
            public ReactionResponse Response { get; } = new();
            public Action<ReactionResponse> OnCompleted { get; }
            public int NextIndex { get; set; }
        }

        private sealed class RootRequest
        {
            public RootRequest(
                GameAction rootAction,
                IReadOnlyList<IGameActionReactor> chainReactors,
                CancellationToken cancellationToken,
                UniTaskCompletionSource<ActionOutcome> completion)
            {
                RootAction = rootAction;
                ChainReactors = chainReactors;
                CancellationToken = cancellationToken;
                Completion = completion;
            }

            public GameAction RootAction { get; }
            public IReadOnlyList<IGameActionReactor> ChainReactors { get; }
            public CancellationToken CancellationToken { get; }
            public UniTaskCompletionSource<ActionOutcome> Completion { get; }
        }

        private sealed class ActiveChain
        {
            private readonly Queue<TraceEntry> _trace;
            private readonly int _traceCapacity;
            private readonly Dictionary<long, ActionRuntime> _runtimes = new();

            public ActiveChain(long id, RootRequest request, int traceCapacity)
            {
                Id = id;
                Request = request;
                _traceCapacity = traceCapacity;
                _trace = new Queue<TraceEntry>(traceCapacity);
            }

            public long Id { get; }
            public RootRequest Request { get; }
            public int ExecutedActionCount { get; set; }
            public bool IsAborted { get; set; }
            public bool DiscardHistory { get; set; }
            public ActionOutcome AbortOutcome { get; set; }
            public ActionOutcome? RootOutcome { get; set; }

            public void AddRuntime(ActionRuntime runtime) => _runtimes.Add(runtime.Id, runtime);

            public bool TryGetRuntime(long id, out ActionRuntime runtime) =>
                _runtimes.TryGetValue(id, out runtime);

            public void AddTrace(string actionName, string cause)
            {
                if (_trace.Count >= _traceCapacity)
                    _trace.Dequeue();

                _trace.Enqueue(new TraceEntry(actionName, cause));
            }

            public string FormatTrace()
            {
                var lines = new List<string>(_trace.Count);
                foreach (TraceEntry entry in _trace)
                    lines.Add(entry.Format());
                return string.Join("\n -> ", lines);
            }
        }

        private readonly struct TraceEntry
        {
            private readonly string _actionName;
            private readonly string _cause;

            public TraceEntry(string actionName, string cause)
            {
                _actionName = actionName;
                _cause = cause;
            }

            public string Format()
            {
                return string.IsNullOrEmpty(_cause)
                    ? _actionName
                    : $"{_actionName} <= {_cause}";
            }
        }

        #endregion
    }
}
