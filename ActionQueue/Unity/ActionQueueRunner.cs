using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CardGame.ActionQueue
{
    /// <summary>
    /// Unity 生命周期适配器。所有队列状态和执行逻辑均由纯 C# ActionQueueEngine 持有。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ActionQueueRunner : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxActionsPerChain = 128;
        [SerializeField, Min(4)] private int traceCapacity = 24;
        [SerializeField] private bool skipPresentationWaits;

        private ActionQueueEngine _engine;

        public ReactorRegistry Reactors => GetOrCreateEngine().Reactors;
        public ReactionGateRegistry ReactionGates => GetOrCreateEngine().ReactionGates;
        public ActionEngineGuardSet EngineGuards => GetOrCreateEngine().EngineGuards;
        public ActionQueueDebugService Debugger => GetOrCreateEngine().Debugger;
        public bool IsRunning => _engine != null && _engine.IsRunning;
        public int PendingRootCount => _engine?.PendingRootCount ?? 0;
        public bool SkipPresentationWaits
        {
            get => skipPresentationWaits;
            set
            {
                skipPresentationWaits = value;
                if (_engine != null)
                    _engine.SkipPresentationWaits = value;
            }
        }

        public UniTask<ActionOutcome> Enqueue(
            GameAction rootAction,
            IReadOnlyList<IGameActionReactor> chainReactors = null,
            CancellationToken cancellationToken = default)
        {
            return GetOrCreateEngine().Enqueue(
                rootAction,
                chainReactors,
                cancellationToken);
        }

        public void StopAndClear()
        {
            _engine?.StopAndClear();
        }

        public ActionQueueDebugSnapshot GetDebugSnapshot()
        {
            return GetOrCreateEngine().GetDebugSnapshot();
        }

        private ActionQueueEngine GetOrCreateEngine()
        {
            if (_engine != null)
                return _engine;

            _engine = new ActionQueueEngine(
                CreateEngineOptions(),
                new UnityActionQueueLogger(this));
            return _engine;
        }

        private void OnDestroy()
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
