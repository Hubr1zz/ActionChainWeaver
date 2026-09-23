#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CardGame.ActionQueue.Benchmarks
{
    /// <summary>
    /// P0 可重复基准。挂到 ActionQueueRunner 同一 GameObject，使用组件菜单运行。
    /// 结果同时写入 Console，并暴露为 LastReport 供自动化或自定义导出读取。
    /// </summary>
    [RequireComponent(typeof(ActionQueueRunner))]
    public sealed class ActionQueueBenchmarkRunner : MonoBehaviour
    {
        private static readonly ProfilerMarker SuiteMarker =
            new("ActionQueue.Benchmark.Suite");
        private static readonly ProfilerMarker NoReactorsMarker =
            new("ActionQueue.Benchmark.NoReactors");
        private static readonly ProfilerMarker ReactorsMarker =
            new("ActionQueue.Benchmark.Reactors");
        private static readonly ProfilerMarker DebugRecordingMarker =
            new("ActionQueue.Benchmark.DebugRecording");

        [SerializeField, Min(0)] private int warmupIterations = 3;
        [SerializeField, Min(1)] private int measuredIterations = 20;
        [SerializeField, Range(1, 120)] private int leafActionsPerChain = 100;
        [SerializeField, Range(1, 32)] private int reactorCount = 10;

        private ActionQueueRunner _queue;
        private bool _isRunning;

        public ActionQueueBenchmarkSuiteReport LastReport { get; private set; }

        private void Awake()
        {
            _queue = GetComponent<ActionQueueRunner>();
        }

        [ContextMenu("ActionQueue/Performance/Run P0 Suite")]
        public void RunP0Suite()
        {
            RunP0SuiteAsync().Forget();
        }

        public async UniTask<ActionQueueBenchmarkSuiteReport> RunP0SuiteAsync()
        {
            if (_isRunning)
                throw new InvalidOperationException("An ActionQueue benchmark is already running.");

            _queue ??= GetComponent<ActionQueueRunner>();
            if (_queue.IsRunning)
            {
                throw new InvalidOperationException(
                    "Wait for the current ActionQueue chain to finish before running P0 benchmarks.");
            }

            if (_queue.Debugger.IsRecording)
            {
                throw new InvalidOperationException(
                    "Close Action Queue Debugger and disable breakpoint mode before running P0 benchmarks.");
            }

            if (!SupportsThreadAllocationCounter())
            {
                Debug.LogWarning(
                    "GC.GetAllocatedBytesForCurrentThread returned no bytes for a known allocation. " +
                    "Ignore the benchmark B/chain field and use Unity Profiler CPU Usage > GC Alloc.",
                    this);
            }

            _isRunning = true;
            SuiteMarker.Begin();
            try
            {
                var results = new List<ActionQueueBenchmarkResult>(3)
                {
                    await MeasureCaseAsync(
                        $"{leafActionsPerChain} leaf actions / no reactors / debug off",
                        reactorsPerAction: 0,
                        recordDebug: false,
                        marker: NoReactorsMarker),
                    await MeasureCaseAsync(
                        $"{leafActionsPerChain} leaf actions / {reactorCount} reactors / debug off",
                        reactorsPerAction: reactorCount,
                        recordDebug: false,
                        marker: ReactorsMarker),
                    await MeasureCaseAsync(
                        $"{leafActionsPerChain} leaf actions / {reactorCount} reactors / debug on",
                        reactorsPerAction: reactorCount,
                        recordDebug: true,
                        marker: DebugRecordingMarker)
                };

                LastReport = new ActionQueueBenchmarkSuiteReport
                {
                    UnityVersion = Application.unityVersion,
                    Platform = Application.platform.ToString(),
                    Results = results.ToArray()
                };

                Debug.Log(LastReport.ToString(), this);
                return LastReport;
            }
            finally
            {
                SuiteMarker.End();
                _isRunning = false;
            }
        }

        private async UniTask<ActionQueueBenchmarkResult> MeasureCaseAsync(
            string name,
            int reactorsPerAction,
            bool recordDebug,
            ProfilerMarker marker)
        {
            var registrations = new List<IDisposable>(reactorsPerAction);
            IDisposable debugLease = null;
            try
            {
                for (int i = 0; i < reactorsPerAction; i++)
                    registrations.Add(_queue.Reactors.RegisterGlobal(new BenchmarkReactor()));

                if (recordDebug)
                    debugLease = _queue.Debugger.AcquireRecording();

                for (int i = 0; i < warmupIterations; i++)
                    await RunOneChainAsync();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long timestampBefore = Stopwatch.GetTimestamp();
                marker.Begin();
                try
                {
                    for (int i = 0; i < measuredIterations; i++)
                        await RunOneChainAsync();
                }
                finally
                {
                    marker.End();
                }
                long timestampAfter = Stopwatch.GetTimestamp();
                long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
                double elapsedMilliseconds =
                    (timestampAfter - timestampBefore) * 1000d / Stopwatch.Frequency;

                return new ActionQueueBenchmarkResult
                {
                    Name = name,
                    Iterations = measuredIterations,
                    LeafActionsPerChain = leafActionsPerChain,
                    ReactorsPerAction = reactorsPerAction,
                    DebugRecording = recordDebug,
                    TotalMilliseconds = elapsedMilliseconds,
                    AllocatedBytes = Math.Max(0L, allocatedAfter - allocatedBefore)
                };
            }
            finally
            {
                debugLease?.Dispose();
                foreach (IDisposable registration in registrations)
                    registration.Dispose();
            }
        }

        private async UniTask RunOneChainAsync()
        {
            ActionOutcome outcome = await _queue.Enqueue(
                new BenchmarkSequenceAction(leafActionsPerChain));
            if (!outcome.IsSuccess)
                throw new InvalidOperationException($"Benchmark chain failed: {outcome}");
        }

        private static bool SupportsThreadAllocationCounter()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var probe = new byte[1024];
            GC.KeepAlive(probe);
            long after = GC.GetAllocatedBytesForCurrentThread();
            return after > before;
        }
    }
}
#endif
