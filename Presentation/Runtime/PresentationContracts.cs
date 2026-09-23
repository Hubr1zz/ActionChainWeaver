using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework.Presentation
{
    /// <summary>表现系统的全局执行模式。与单个 Request 的冲突策略相互独立。</summary>
    public enum PresentationExecutionMode
    {
        /// <summary>正常调度并执行 Handler。</summary>
        Normal,

        /// <summary>
        /// 调用 Handler 将表现同步推进到终态；成功后 Handle 以 CompletedImmediately 完成。
        /// </summary>
        CompleteImmediately
    }

    public enum PresentationConflictPolicy
    {
        /// <summary>在同一通道中按提交顺序等待执行。</summary>
        Queue,

        /// <summary>取消同一通道中正在运行和尚未开始的旧表现，只保留最新请求。</summary>
        ReplaceCurrent,

        /// <summary>同一通道繁忙时忽略新请求；返回的 Handle 会立即以 Skipped 完成。</summary>
        IgnoreNew
    }

    public enum PresentationStatus
    {
        Pending,
        Running,
        Completed,
        CompletedImmediately,
        Skipped,
        Cancelled,
        Faulted
    }

    /// <summary>
    /// 冲突隔离键。例如 (Card#17, "Position") 与 (Card#17, "Hover") 属于不同通道。
    /// Owner 使用引用身份比较，Name 使用 Ordinal 字符串比较。
    /// </summary>
    public readonly struct PresentationChannel : IEquatable<PresentationChannel>
    {
        public PresentationChannel(object owner, string name)
        {
            Owner = owner;
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Presentation channel name cannot be empty.", nameof(name))
                : name;
        }

        public object Owner { get; }
        public string Name { get; }

        public bool Equals(PresentationChannel other)
        {
            return ReferenceEquals(Owner, other.Owner) &&
                   string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) =>
            obj is PresentationChannel other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int ownerHash = Owner == null ? 0 : RuntimeHelpers.GetHashCode(Owner);
                return (ownerHash * 397) ^ StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
            }
        }

        public override string ToString() =>
            $"{Owner?.GetType().Name ?? "Global"}/{Name}";
    }

    /// <summary>
    /// 一次表现需求的不可变描述。具体子类自行声明伤害值、目标、路径等业务参数。
    /// Request 不决定调用者是否等待；它只声明该表现与同通道表现发生冲突时如何处理。
    /// </summary>
    public abstract class PresentationRequest
    {
        protected PresentationRequest(
            PresentationChannel channel,
            PresentationConflictPolicy conflictPolicy)
        {
            Channel = channel;
            ConflictPolicy = conflictPolicy;
        }

        public PresentationChannel Channel { get; }
        public PresentationConflictPolicy ConflictPolicy { get; }
    }

    public readonly struct PresentationOutcome
    {
        public PresentationOutcome(
            PresentationStatus status,
            string reason = null,
            Exception exception = null)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            Exception = exception;
        }

        public PresentationStatus Status { get; }
        public string Reason { get; }
        public Exception Exception { get; }
        public bool IsCompleted => Status is PresentationStatus.Completed or
            PresentationStatus.CompletedImmediately;
        public bool WasCompletedImmediately =>
            Status == PresentationStatus.CompletedImmediately;
    }

    /// <summary>运行中表现实例的公开句柄；Completion 始终代表真实表现生命周期。</summary>
    public sealed class PresentationHandle
    {
        private readonly UniTaskCompletionSource<PresentationOutcome> _completion = new();
        private Action _cancel;

        internal PresentationHandle(long id, PresentationRequest request)
        {
            Id = id;
            Request = request;
            Status = PresentationStatus.Pending;
        }

        public long Id { get; }
        public PresentationRequest Request { get; }
        public PresentationStatus Status { get; private set; }
        public PresentationOutcome Outcome { get; private set; }
        public UniTask<PresentationOutcome> Completion => _completion.Task;
        public bool IsFinished => Status is PresentationStatus.Completed or
            PresentationStatus.CompletedImmediately or
            PresentationStatus.Skipped or PresentationStatus.Cancelled or PresentationStatus.Faulted;
        public bool WasCompletedImmediately =>
            Status == PresentationStatus.CompletedImmediately;

        public void Cancel() => _cancel?.Invoke();

        public async UniTask WaitForCompletionAsync()
        {
            await _completion.Task;
        }

        internal void BindCancel(Action cancel)
        {
            _cancel = cancel;
        }

        internal void MarkRunning()
        {
            if (Status == PresentationStatus.Pending)
                Status = PresentationStatus.Running;
        }

        internal void Complete(PresentationOutcome outcome)
        {
            if (IsFinished)
                return;

            Outcome = outcome;
            Status = outcome.Status;
            _cancel = null;
            _completion.TrySetResult(outcome);
        }
    }

    public interface IPresentationHandler
    {
        Type RequestType { get; }

        UniTask PresentAsync(
            PresentationRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// 同步建立表现并推进到最终状态。Tween 实现应调用 Complete(true)，
        /// 使终值和完成回调都在返回前生效。
        /// </summary>
        void CompleteImmediately(PresentationRequest request);
    }

    public abstract class PresentationHandler<TRequest> : IPresentationHandler
        where TRequest : PresentationRequest
    {
        public Type RequestType => typeof(TRequest);

        UniTask IPresentationHandler.PresentAsync(
            PresentationRequest request,
            CancellationToken cancellationToken)
        {
            return PresentAsync((TRequest)request, cancellationToken);
        }

        void IPresentationHandler.CompleteImmediately(PresentationRequest request)
        {
            CompleteImmediately((TRequest)request);
        }

        protected abstract UniTask PresentAsync(
            TRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// 必须把表现同步推进到与正常播放完成相同的最终视觉状态。
        /// 对 DOTween，请创建/取得 Tween 后调用 tween.Complete(withCallbacks: true)。
        /// </summary>
        protected abstract void CompleteImmediately(TRequest request);
    }
}
