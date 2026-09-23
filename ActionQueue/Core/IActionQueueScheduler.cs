namespace CardGame.ActionQueue
{
    /// <summary>ActionExecutionContext 使用的最小调度能力，避免反向依赖 Unity Adapter。</summary>
    internal interface IActionQueueScheduler
    {
        void EnqueueFromCurrentAction(
            GameAction action,
            bool immediate,
            long parentActionId,
            string cause);
    }
}
