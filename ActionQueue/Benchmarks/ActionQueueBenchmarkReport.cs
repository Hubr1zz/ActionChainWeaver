#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Text;

namespace CardGame.ActionQueue.Benchmarks
{
    [Serializable]
    public sealed class ActionQueueBenchmarkResult
    {
        public string Name;
        public int Iterations;
        public int LeafActionsPerChain;
        public int ReactorsPerAction;
        public bool DebugRecording;
        public double TotalMilliseconds;
        public long AllocatedBytes;

        public double MillisecondsPerChain =>
            Iterations == 0 ? 0d : TotalMilliseconds / Iterations;

        public double BytesPerChain =>
            Iterations == 0 ? 0d : (double)AllocatedBytes / Iterations;
    }

    [Serializable]
    public sealed class ActionQueueBenchmarkSuiteReport
    {
        public string UnityVersion;
        public string Platform;
        public ActionQueueBenchmarkResult[] Results;

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine("[ActionQueue Benchmark]")
                .Append("Unity ").Append(UnityVersion)
                .Append(" | ").AppendLine(Platform);

            if (Results == null)
                return builder.ToString();

            foreach (ActionQueueBenchmarkResult result in Results)
            {
                builder.Append(result.Name)
                    .Append(" | ").Append(result.MillisecondsPerChain.ToString("F4"))
                    .Append(" ms/chain | ").Append(result.BytesPerChain.ToString("F0"))
                    .Append(" B/chain | total ").Append(result.TotalMilliseconds.ToString("F2"))
                    .Append(" ms, ").Append(result.AllocatedBytes).AppendLine(" B");
            }

            return builder.ToString();
        }
    }
}
#endif
