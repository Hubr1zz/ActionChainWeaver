# PresentationSystem

表现系统与 `ActionQueue` 分离：`GameAction` 表示可被 Reactor 响应的战斗事实；
`PresentationRequest` 表示动画、音效、飘字等事实投影，不进入 ActionQueue，也不触发 Reactor。

## Request 参数

每种具体 Request 自己声明所需参数：

```csharp
public sealed class DamagePresentationRequest : PresentationRequest
{
    public DamagePresentationRequest(
        object target,
        float amount,
        PresentationConflictPolicy policy)
        : base(new PresentationChannel(target, "Damage"), policy)
    {
        Target = target;
        Amount = amount;
    }

    public object Target { get; }
    public float Amount { get; }
}
```

对应 Handler 负责解释这些参数并返回真实异步完成时间：

```csharp
public sealed class DamagePresentationHandler
    : PresentationHandler<DamagePresentationRequest>
{
    protected override async UniTask PresentAsync(
        DamagePresentationRequest request,
        CancellationToken cancellationToken)
    {
        await PlayDamageAnimation(request, cancellationToken);
    }

    protected override void CompleteImmediately(
        DamagePresentationRequest request)
    {
        // 必须落到与正常播放结束完全相同的视觉状态。
        CreateDamageTween(request).Complete(withCallbacks: true);
    }
}
```

## 是否等待由 Action 决定

```csharp
PresentationHandle handle = dispatcher.Publish(request, cancellationToken);

// 需要等待：使用 ActionExecutionContext，以便 Debug 配置可统一跳过等待。
await context.AwaitPresentationAsync(handle.WaitForCompletionAsync());

// 不需要等待：只发布，不 await。
dispatcher.Publish(request, cancellationToken);
```

`ActionQueueRunner.SkipPresentationWaits` 只让 `AwaitPresentationAsync` 立即返回；
表现仍会执行，`PresentationHandle.Completion` 仍然只在真实表现结束、被忽略、取消或失败后完成。

## 冲突策略

- `Queue`：同通道顺序执行。
- `ReplaceCurrent`：取消同通道的当前表现与待执行旧请求，只保留最新请求。
- `IgnoreNew`：通道繁忙时，新 Handle 立即以 `Skipped` 完成。

冲突策略只决定 Handle 的真实结束时间，不决定 Action 是否等待。

## Debug：立即完成

```csharp
dispatcher.ExecutionMode = PresentationExecutionMode.CompleteImmediately;
PresentationHandle handle = dispatcher.Publish(request);

// Handler 已同步把 Tween 推到终点，Handle 随后终结。
Debug.Assert(handle.Status == PresentationStatus.CompletedImmediately);
Debug.Assert(handle.WasCompletedImmediately);
```

`CompleteImmediately` 是表现系统的全局执行模式：新发布的 Request 不进入通道，
但仍调用对应 Handler 的 `CompleteImmediately`。Handler 必须同步建立/取得表现并推进到
最终状态；DOTween 应使用 `tween.Complete(withCallbacks: true)`，确保终值和 `OnComplete`
等回调都已执行。该方法成功返回后，Handle 才会变为 `CompletedImmediately`；若它抛出异常，
Handle 会变为 `Faulted`。因此 Action 即使选择 `await`，也会在最终视觉状态已落实后立即继续。
模式只在 `Publish` 时取快照，切换模式不会伪造此前已经发布的表现已经结束。

它与 `ActionQueueRunner.SkipPresentationWaits` 的区别：

- `CompleteImmediately`：Handler 同步把表现推到终态，不按原时长播放，Handle 随后报告 `CompletedImmediately`。
- `SkipPresentationWaits`：表现照常播放，Handle 保持真实状态，但 Action 不等待。
