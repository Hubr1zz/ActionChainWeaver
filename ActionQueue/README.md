# ActionQueue

第一次接触本系统时，建议先阅读 [ActionQueue 新手简介](GETTING_STARTED.md)。性能基准与优化路线单独维护在 [ActionQueue 性能优化审查](PERFORMANCE.md)。

这是一个与旧 `CardGame.ActionSystem` 并存的新实现。运行时没有 Action 执行器递归：

```text
外部命令 -> Root FIFO
                |
                v
         当前 Chain 双端队列
         [Action / Continuation / Action ...]
```

Composite Action 被拆成“一个子 Action + 一个 continuation”。子节点结束后，continuation 再询问父节点的下一个子 Action。嵌套 Composite 仍使用相同机制，因此父 Action 可以安全地成为另一个 Action 的子节点。

## Reactor 来源

| 来源 | API | 用途 |
|---|---|---|
| 全局 | `runner.Reactors.RegisterGlobal` | 全局规则、战斗场景规则 |
| 实体 | `RegisterForEntity(entity, reactor, relation)` | 角色状态、敌人状态、遗物；按 Source/Target 路由 |
| 当前链 | `runner.Enqueue(root, chainReactors)` | 本次出牌、事件或攻击流程内的临时效果 |
| Action 子树 | `action.AddSubtreeReactor` | 当前 Action 自身及它产生的全部因果后代 |
| Action 后代 | `action.AddDescendantReactor` | 只影响当前 Action 的因果后代，不影响自身 |
| 单 Action | `action.AddLocalReactor` | 只影响某一个 Action 实例的临时覆盖 |

实体 Reactor 是“攻击不同敌人得到不同响应”的关键。伤害或攻击 Action 暴露 `Source` 和 `Target`；只会收集这两个实体上符合 relation 的 Reactor，不会扫描并触发其他敌人的效果。

`GameAction` 基类不假定所有 Action 都有参与实体。领域 Action 按需实现 `ISourceAction`、
`ITargetAction` 或 `IMultiTargetAction`；例如 `DamageAction` 自己维护 Source、Target、伤害种类、标签和来源。

Action 运行时分类为 `Command`、`Signal`、`Composite`。`SignalAction` 只发布已发生的游戏逻辑事实，
默认只开放 `AfterResolved`；表现不属于此分类，统一交给独立 PresentationSystem。

同一阶段内先按监听 Action 类型从具体到抽象执行（子类 Reactor 先于父类 Reactor），再在相同类型层级内按 `Priority` 从高到低、注册顺序从早到晚执行。Registry 注册 API 返回 `IDisposable`，实体死亡、状态移除或场景退出时应及时释放。子树/后代 Reactor 不进入 Registry，而是随 `ActionRuntime` 继承；Chain 完成、失败或取消后自然释放，无需另外安排“注销 Action”。

每个 Reactor 提供稳定 `Key` 和玩法 `Tags`。`ReactionGateRegistry` 可按 Action、Reactor Key/Tag、
Source/Target、结果等信息选择性禁止响应，例如只禁止 `Counterattack`，而保留吸血、统计和日志。
`ObservedActionType` 做类型粗筛，`ReactionGate` 做外部准入，`Matches` 做 Reactor 自身条件判断。

系统不变量不使用 Reactor。`ActionEngineGuardSet` 在调度前执行不可被玩法 Buff 屏蔽的基础设施验证，
不生成后续 Action，也不参与玩法响应排序。

## 阶段和结果

每个 Action 依次经历：

```text
BeforeExecution -> Execute/Composite children -> AfterResolved
```

结果明确区分：

- `Succeeded`：效果成功发生；
- `Failed`：行为执行了，但规则判定失败；
- `Prevented`：前置规则令行为无效；
- `Cancelled`：玩家主动取消或外部取消。

异常属于程序错误，由 Engine 记录后中止当前根链，不伪装成玩家取消。

## AddToTop / AddToBottom

Reactor 使用 `response.EnqueueImmediate` 生成的 Action 会在父 Composite continuation 之前执行，适合反击、荆棘等即时响应。`EnqueueToBottom` 则追加到当前根链尾部。

Action 本身也可以通过 `ActionExecutionContext` 使用这两个入口，但不得直接调用或等待另一个 Action。

## 单目标与多目标路由

普通 Action 继续通过单值 `Target` 路由，不创建目标集合。只有 AOE 等“整体多目标意图”实现
`IMultiTargetAction.Targets`；此时 Registry 将该列表视为完整目标集合，并忽略单值 `Target`。

多目标父 Action 的 `BeforeExecution` 会先收集每个目标实体上的 Reactor。任一目标都可以在第一段
单体伤害产生前 Prevent 整个父 Action。真正的 HP 修改仍应拆成父 Composite 下的单目标
`DamageAction`，使每个目标的护甲、闪避和反伤保持独立结算。

`ReactionContext.MatchedEntity` 表示本次实体路由匹配到谁，`TargetIndex` 表示其在多目标列表中的
首次下标。重复目标会按同一注册去重；需要多次命中同一目标时，应产生多个 DamageAction。

## 表现等待

动画、音效和飘字不作为 GameAction。独立 `PresentationSystem` 发布请求并返回真实生命周期 Handle；
Action 自己决定是否等待：

```csharp
PresentationHandle handle = dispatcher.Publish(request, cancellationToken);
await context.AwaitPresentationAsync(handle.WaitForCompletionAsync());
```

不需要等待时只发布即可。`ActionQueueRunner.SkipPresentationWaits` 可在 Debug 时统一让上述等待立即
继续，但不会取消表现或伪造 Handle 的完成状态。

## 循环保护

`ActionQueueEngine.MaxActionsPerChain` 是一次根流程允许进入的最大 Action 数；Unity Adapter 从
`ActionQueueRunner.maxActionsPerChain` 构造该配置。它同时覆盖直接自循环和
`Damage -> Draw -> Damage` 之类的间接循环。超过预算后会：

1. 中止当前根链；
2. 返回 `Failed`；
3. 用 `Debug.LogWarning` 输出最近的 Action/Reaction 因果轨迹；
4. 继续处理下一个根请求。

这个预算应高于游戏中最大的合法多段结算数量，并在自动化测试中覆盖合法上界。

## 日志级别

`ActionQueueRunner.LogLevel` 和 `ActionQueueEngine.LogLevel` 提供三个级别：

| 级别 | Runner 输出 |
|---|---|
| `None` | 完全不输出 Runner 日志；异常仍会转化为失败结果，循环保护仍会中止 Chain |
| `WarningsAndErrors` | 只输出未处理异常和循环上限警告，默认值 |
| `Verbose` | 在警告与异常之外，输出每个 Action 的结算结果 |

该设置只控制 ActionQueue Engine 自身，不会拦截业务 Action、Reactor、示例脚本或 Benchmark 主动调用的 `Debug.Log`。
运行时也可以通过 `runner.LogLevel` 修改。旧版 `verboseLogging` 会自动迁移：关闭对应
`WarningsAndErrors`，开启对应 `Verbose`。

## 示例

给一个 GameObject 添加 `ActionQueueRunner` 和 `ActionQueueExamples`，在组件右键菜单运行：

1. 成功攻击并回血；
2. Boss 前置判定阻止攻击；
3. 力量判定失败后 Boss 反击；
4. 攻击另一个敌人，不触发 Boss 的实体 Reactor；
5. `Damage <-> Draw` 间接循环被链预算终止。

GameAction 实例是一次性的。每次入队都必须创建新对象，避免保留上次执行的 Prevention、输入和 Composite 进度。

## 可视化 Debugger

调试代码集中在独立目录中：

```text
Debugging/
├── ActionQueueDebugService.cs       # 断点、树记录、历史与快照
├── ActionQueueDebugSnapshot.cs      # 只读调试模型
└── ActionQueueEngine.Debug.cs       # Engine 的调试 partial

Editor/
└── ActionQueueDebuggerWindow.cs     # Unity Editor 可视化窗口
```

`ActionQueueRunner.cs` 不保存队列、调试树、断点等待器或历史快照。Engine 只在
Action/Reactor 生命周期节点通知调试服务；调试入口统一由 `runner.Debugger` 提供。
Engine 中必须访问私有队列状态的调试代码单独放在 partial 文件，并使用
`#region Debug Integration` 包裹。`ReactorRegistry` 中仅存的一段调试枚举支持也使用
`#region Debug Support` 隔离。

调试服务是延迟创建的。窗口打开时通过记录租约启用节点采集，窗口关闭后释放租约并清理记录；
未打开窗口且未启用断点时，正式结算不会构造调试树。窗口在一条 Chain 执行到一半时才打开，
只能看到此后产生的节点，这是按需记录的预期行为。

在 Unity 菜单打开：

```text
Window -> Card Game -> Action Queue Debugger
```

进入 Play Mode 后，窗口可以：

- 切换场景中的不同 `ActionQueueRunner`；
- 在左侧查看待处理根 Action、内部工作队列和所有已注册 Reactor；
- 在中间查看带直角层级连接线的完整 Action/Reactor 因果树；
- 点击树中的任意 Action 或 Reactor，在最右侧查看完整节点信息；
- 查看当前 Chain 或上一条已完成 Chain；
- 查看节点的 Queued、Executing、Resolved、Skipped 状态和最终 Outcome；
- 开启“断点模式”，在 `Action.Before`、每个匹配 Reactor、`Action.Execute` 前暂停；
- 点击“继续下一个节点”只放行一个可见节点；该步骤新插入的节点会显示橙色 `NEW` 标记；
- 点击“停止并清除”取消当前 Chain、完成并移除当时待处理的根请求，同时清空调试历史。

Composite 与 Reaction 的内部 continuation 会自动通过，不作为断点节点，避免调试时停在队列实现细节上。如果在某个异步输入 Action 执行期间开启断点模式，Runner 会在该 Action 完成后的下一个节点暂停。

运行时代码也可以调用 `runner.StopAndClear()`。它通过内部 linked cancellation token
取消断点等待和遵守取消令牌的异步 Action；无法强制终止一段正在主线程同步执行、或完全忽略
`CancellationToken` 的第三方代码。

## 代码模块

```text
Core/
├── ActionQueueEngine.cs             # Root FIFO、执行泵与公开入口
├── ActionQueueEngine.Actions.cs     # Action 与 Composite 结算
├── ActionQueueEngine.Reactions.cs   # Reactor 批次与衍生 Action 排队
├── ActionQueueEngine.WorkItems.cs   # 工作项状态机
├── ActionQueueEngine.Logging.cs     # 日志策略，不绑定日志框架
├── ActionQueueLogLevel.cs           # 日志级别定义
├── ActionQueueOptions.cs            # Engine 构造配置
├── IActionQueueLogger.cs            # 宿主日志契约
└── ArrayDeque.cs                    # 无节点分配的双端队列

Unity/
├── ActionQueueRunner.cs             # MonoBehaviour 薄 Adapter
├── ActionQueueRunner.Logging.cs     # Inspector 配置与旧字段迁移
└── UnityActionQueueLogger.cs        # Unity Console 适配

Debugging/                           # Engine 运行时诊断，按需启用
Editor/                              # 仅 Unity Editor 的窗口
Benchmarks/                          # 仅 Editor / Development Build 的 P0 基准
Examples/                            # 可运行示例
```

`Core + Reactions + Debugging` 不引用 `UnityEngine`，但为了当前 Unity 使用场景继续使用 UniTask。
普通 .NET 移植时需要替换的异步边界和文件清单见 [移植说明](PORTING.md)。

性能热点、基准运行方法和后续 P2 候选统一维护在 [ActionQueue 性能优化审查](PERFORMANCE.md)，不再混入新手简介。
