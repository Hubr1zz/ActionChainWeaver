using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameFramework.Preview
{
    /// <summary>一条无副作用的结果预览规则。</summary>
    public interface IPreviewRule<in TInput, TResult>
    {
        string Id { get; }
        int Priority { get; }
        TResult Evaluate(TInput input, TResult current, PreviewTrace trace);
    }

    public sealed class PreviewTrace
    {
        private readonly List<PreviewTraceEntry> _entries = new();
        private readonly ReadOnlyCollection<PreviewTraceEntry> _view;

        public PreviewTrace()
        {
            _view = _entries.AsReadOnly();
        }

        public IReadOnlyList<PreviewTraceEntry> Entries => _view;

        public void Add(string ruleId, string message)
        {
            _entries.Add(new PreviewTraceEntry(ruleId, message));
        }
    }

    public readonly struct PreviewTraceEntry
    {
        public PreviewTraceEntry(string ruleId, string message)
        {
            RuleId = ruleId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string RuleId { get; }
        public string Message { get; }
    }

    public readonly struct PreviewResult<TResult>
    {
        public PreviewResult(TResult value, PreviewTrace trace)
        {
            Value = value;
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public TResult Value { get; }
        public PreviewTrace Trace { get; }
    }

    /// <summary>
    /// 预览单个计算结果，例如费用、伤害或合法性；不会遍历未来 Action 节点。
    /// </summary>
    public sealed class PreviewPipeline<TInput, TResult>
    {
        private readonly Func<TInput, TResult> _baseline;
        private readonly List<Registration> _rules = new();
        private long _nextOrder;
        private bool _evaluating;

        public PreviewPipeline(Func<TInput, TResult> baseline)
        {
            _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        }

        public IDisposable Register(IPreviewRule<TInput, TResult> rule)
        {
            ThrowIfEvaluating();
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            var registration = new Registration(this, rule, _nextOrder++);
            _rules.Add(registration);
            _rules.Sort(RegistrationComparer.Instance);
            return registration;
        }

        public PreviewResult<TResult> Evaluate(TInput input)
        {
            if (_evaluating)
                throw new InvalidOperationException("PreviewPipeline does not support recursive evaluation.");

            _evaluating = true;
            try
            {
                TResult result = _baseline(input);
                var trace = new PreviewTrace();
                foreach (Registration registration in _rules)
                    result = registration.Rule.Evaluate(input, result, trace);
                return new PreviewResult<TResult>(result, trace);
            }
            finally
            {
                _evaluating = false;
            }
        }

        private void Remove(Registration registration)
        {
            ThrowIfEvaluating();
            _rules.Remove(registration);
        }

        private void ThrowIfEvaluating()
        {
            if (_evaluating)
            {
                throw new InvalidOperationException(
                    "Preview rule registrations cannot change during evaluation.");
            }
        }

        private sealed class Registration : IDisposable
        {
            private PreviewPipeline<TInput, TResult> _owner;

            public Registration(
                PreviewPipeline<TInput, TResult> owner,
                IPreviewRule<TInput, TResult> rule,
                long order)
            {
                _owner = owner;
                Rule = rule;
                Order = order;
            }

            public IPreviewRule<TInput, TResult> Rule { get; }
            public long Order { get; }

            public void Dispose()
            {
                PreviewPipeline<TInput, TResult> owner = _owner;
                if (owner == null)
                    return;
                owner.ThrowIfEvaluating();
                _owner = null;
                owner.Remove(this);
            }
        }

        private sealed class RegistrationComparer : IComparer<Registration>
        {
            public static readonly RegistrationComparer Instance = new();

            public int Compare(Registration x, Registration y)
            {
                int priority = y.Rule.Priority.CompareTo(x.Rule.Priority);
                return priority != 0 ? priority : x.Order.CompareTo(y.Order);
            }
        }
    }
}
