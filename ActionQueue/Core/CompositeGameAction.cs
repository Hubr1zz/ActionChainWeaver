using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue
{
    /// <summary>
    /// Composite 并不递归执行子节点。Runner 会把子 Action 和 continuation 放入同一条双端队列。
    /// 因而 Composite 本身也可以安全地作为另一个 Composite 的子节点。
    /// </summary>
    public abstract class CompositeGameAction : GameAction
    {
        public sealed override ActionExecutionKind ExecutionKind => ActionExecutionKind.Composite;
        internal GameAction GetNextChildInternal(CompositeExecutionContext context) =>
            GetNextChild(context);

        internal ActionOutcome ResolveInternal(CompositeExecutionContext context) =>
            Resolve(context);

        protected abstract GameAction GetNextChild(CompositeExecutionContext context);

        protected virtual ActionOutcome Resolve(CompositeExecutionContext context)
        {
            if (context.CompletedOutcomes.Count == 0)
                return ActionOutcome.Success();

            return context.CompletedOutcomes[context.CompletedOutcomes.Count - 1];
        }

        protected sealed override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            // Composite 由 ActionQueueEngine 展开，这个方法永远不应被调用。
            return UniTask.FromResult(
                ActionOutcome.Failure("Composite was executed as an atomic action."));
        }
    }

    public sealed class CompositeExecutionContext
    {
        private readonly List<ActionOutcome> _completedOutcomes = new();

        public IReadOnlyList<ActionOutcome> CompletedOutcomes => _completedOutcomes;
        public int CompletedCount => _completedOutcomes.Count;
        public ActionOutcome LastOutcome =>
            _completedOutcomes.Count == 0 ? default : _completedOutcomes[_completedOutcomes.Count - 1];

        internal void AddOutcome(ActionOutcome outcome)
        {
            _completedOutcomes.Add(outcome);
        }
    }
}
