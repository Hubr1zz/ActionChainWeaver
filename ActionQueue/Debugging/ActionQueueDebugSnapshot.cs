using System.Collections.Generic;

namespace CardGame.ActionQueue
{
    public enum ActionQueueDebugNodeKind
    {
        Action,
        Reactor
    }

    public enum ActionQueueDebugNodeState
    {
        Queued,
        Executing,
        Resolved,
        Skipped
    }

    public sealed class ActionQueueDebugNode
    {
        internal ActionQueueDebugNode(
            long id,
            long parentActionId,
            ActionQueueDebugNodeKind kind,
            string name,
            string detail,
            ActionExecutionKind? executionKind = null,
            ReactionPhases reactionPhases = ReactionPhases.None)
        {
            Id = id;
            ParentActionId = parentActionId;
            Kind = kind;
            Name = name;
            Detail = detail ?? string.Empty;
            ExecutionKind = executionKind;
            ReactionPhases = reactionPhases;
            State = ActionQueueDebugNodeState.Queued;
        }

        public long Id { get; }
        public long ParentActionId { get; }
        public ActionQueueDebugNodeKind Kind { get; }
        public string Name { get; }
        public string Detail { get; internal set; }
        public ActionExecutionKind? ExecutionKind { get; }
        public ReactionPhases ReactionPhases { get; }
        public ActionQueueDebugNodeState State { get; internal set; }
        public string Outcome { get; internal set; } = string.Empty;
        public List<ActionQueueDebugNode> Children { get; } = new();
        public List<ActionQueueDebugNode> Reactors { get; } = new();
    }

    public sealed class ActionQueueDebugSnapshot
    {
        public bool HasChain { get; internal set; }
        public bool IsLastCompletedChain { get; internal set; }
        public long ChainId { get; internal set; }
        public int ExecutedActionCount { get; internal set; }
        public int MaxActionsPerChain { get; internal set; }
        public bool BreakpointMode { get; internal set; }
        public bool IsPaused { get; internal set; }
        public string PausedNode { get; internal set; } = string.Empty;
        public string CurrentNode { get; internal set; } = string.Empty;
        public List<string> PendingRoots { get; } = new();
        public List<string> PendingWorkItems { get; } = new();
        public List<string> RegisteredReactors { get; } = new();
        public List<ActionQueueDebugNode> Roots { get; } = new();
    }
}
