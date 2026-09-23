using System;

namespace CardGame.ActionQueue
{
    public enum ActionStatus
    {
        Succeeded,
        Failed,
        Prevented,
        Cancelled
    }

    public readonly struct ActionOutcome
    {
        public ActionStatus Status { get; }
        public string Reason { get; }

        public bool IsSuccess => Status == ActionStatus.Succeeded;

        public ActionOutcome(ActionStatus status, string reason = null)
        {
            Status = status;
            Reason = reason ?? string.Empty;
        }

        public static ActionOutcome Success(string reason = null) =>
            new(ActionStatus.Succeeded, reason);

        public static ActionOutcome Failure(string reason = null) =>
            new(ActionStatus.Failed, reason);

        public static ActionOutcome Prevented(string reason = null) =>
            new(ActionStatus.Prevented, reason);

        public static ActionOutcome Cancelled(string reason = null) =>
            new(ActionStatus.Cancelled, reason);

        public override string ToString()
        {
            return string.IsNullOrEmpty(Reason) ? Status.ToString() : $"{Status}: {Reason}";
        }
    }
}
