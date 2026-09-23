namespace CardGame.ActionQueue
{
    /// <summary>
    /// 只控制 ActionQueueEngine 自身产生的日志，不拦截业务 Action、示例或 Reactor 的日志。
    /// </summary>
    public enum ActionQueueLogLevel
    {
        None = 0,
        WarningsAndErrors = 1,
        Verbose = 2
    }
}
