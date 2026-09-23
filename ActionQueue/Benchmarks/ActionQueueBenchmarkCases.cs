#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CardGame.ActionQueue.Benchmarks
{
    internal sealed class BenchmarkLeafAction : CommandAction
    {
        protected override UniTask<ActionOutcome> ExecuteAsync(
            ActionExecutionContext context,
            CancellationToken cancellationToken)
        {
            return UniTask.FromResult(ActionOutcome.Success());
        }
    }

    internal sealed class BenchmarkSequenceAction : CompositeGameAction
    {
        private readonly int _leafCount;

        public BenchmarkSequenceAction(int leafCount)
        {
            _leafCount = leafCount;
        }

        protected override GameAction GetNextChild(CompositeExecutionContext context)
        {
            return context.CompletedCount < _leafCount
                ? new BenchmarkLeafAction()
                : null;
        }
    }

    internal sealed class BenchmarkReactor : GameActionReactor<BenchmarkLeafAction>
    {
        public override ReactionTiming Timing => ReactionTiming.AfterResolved;

        protected override void React(
            BenchmarkLeafAction action,
            ReactionContext context,
            ReactionResponse response)
        {
            // 故意为空：基准只测量匹配、排序与派发成本。
        }
    }
}
#endif
