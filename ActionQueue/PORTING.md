# ActionQueue 移植与依赖边界

当前版本以 Unity 为主要运行环境，因此保留 UniTask；文件结构已经把 UnityEngine 依赖和队列算法分开，但没有强迫所有使用者改用 `Task`。

## 当前边界

```text
Core + Reactions + Debugging
        │
        ├── 依赖 System.*
        └── 依赖 Cysharp.Threading.Tasks

Unity/
        ├── 依赖 UnityEngine
        └── 创建并转发 ActionQueueEngine

Editor/
        └── 依赖 UnityEditor，只负责可视化
```

在 Unity 中既可以继续把 `ActionQueueRunner` 挂到 GameObject，也可以直接创建 Engine：

```csharp
using var engine = new ActionQueueEngine(new ActionQueueOptions
{
    MaxActionsPerChain = 128,
    LogLevel = ActionQueueLogLevel.None
});

ActionOutcome result = await engine.Enqueue(new MyAction());
```

不提供 `IActionQueueLogger` 时 Engine 保持静默，但 Outcome、循环保护和异常中止语义不变。

## 改成普通 .NET 核心时需要替换什么

异步类型集中在以下文件：

1. `Core/GameAction.cs`：Action 的 `ExecuteAsync` 契约。
2. `Core/CompositeGameAction.cs`：Composite 的不可直接执行实现。
3. `Core/ActionQueueEngine.cs`：公开 Enqueue、执行泵和完成源。
4. `Core/ActionQueueEngine.Actions.cs`：异步 Action 执行。
5. `Core/ActionQueueEngine.WorkItems.cs`：工作项异步派发与 Root completion。
6. `Debugging/ActionQueueDebugService.cs`、`ActionQueueEngine.Debug.cs`：断点等待。

可将 `UniTask<T>` 替换为 `ValueTask<T>` 或 `Task<T>`，将 `UniTaskCompletionSource<T>` 替换为
`TaskCompletionSource<T>`。队列顺序、Composite continuation、Reactor 路由和循环保护不需要重写。

移植到多线程服务器时还要单独决定线程模型。当前 Engine 是单执行泵设计，不承诺多个线程同时调用
`Enqueue`、注册 Reactor 或释放注册的安全性；仅替换异步类型不会自动获得线程安全。
