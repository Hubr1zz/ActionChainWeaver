using System;

namespace CardGame.ActionQueue
{
    [Flags]
    public enum RootInputBlockers
    {
        None = 0,
        LogicBusy = 1,
        PresentationBusy = 2
    }

    /// <summary>
    /// 外部输入提交 root Action 前的可选准入策略。当前 Chain 的子 Action 和 Reactor 响应不经过此门。
    /// </summary>
    public sealed class RootActionInputGate
    {
        private readonly Func<bool> isLogicBusy;
        private readonly Func<bool> isPresentationBusy;
        private RootInputBlockers blockWhen;

        public RootActionInputGate(Func<bool> isLogicBusy, Func<bool> isPresentationBusy)
        {
            this.isLogicBusy = isLogicBusy ?? throw new ArgumentNullException(nameof(isLogicBusy));
            this.isPresentationBusy = isPresentationBusy ?? throw new ArgumentNullException(nameof(isPresentationBusy));
        }

        public RootInputBlockers BlockWhen
        {
            get => blockWhen;
            set
            {
                if ((value & ~(RootInputBlockers.LogicBusy | RootInputBlockers.PresentationBusy)) != 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                blockWhen = value;
            }
        }

        public RootInputBlockers GetBlockingReasons()
        {
            RootInputBlockers reasons = RootInputBlockers.None;
            if ((BlockWhen & RootInputBlockers.LogicBusy) != 0 && isLogicBusy())
                reasons |= RootInputBlockers.LogicBusy;
            if ((BlockWhen & RootInputBlockers.PresentationBusy) != 0 && isPresentationBusy())
                reasons |= RootInputBlockers.PresentationBusy;

            return reasons;
        }
    }
}
