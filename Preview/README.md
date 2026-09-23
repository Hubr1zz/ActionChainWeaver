# PreviewSystem

预览系统包含两种不同能力：

- `PreviewPipeline<TInput,TResult>`：不改状态地预览一个计算结果，例如费用、伤害或合法性；
- `SimulationPreview<TNode>`：通过项目提供的纯模拟模型展开未来节点树。

两者都不会执行 `ActionQueue`，也不会自动触发 Reactor。确认操作时，真实 Action 必须基于当前权威状态
再次校验，不能直接采用旧 Preview 的结果。

## 深度

```csharp
var options = new SimulationPreviewOptions
{
    MaxDepth = 1, // 玩家预览默认值：只看一级子 Action
    MaxNodes = 1024
};
```

`MaxDepth=-1` 仅保留给开发调试，仍受 `MaxNodes` 保护。玩家 UI 应使用默认深度 1，不能把调试树
直接展示给玩家。

`ISimulationExpander<TNode>` 必须操作模拟状态快照，不能执行真实 `GameAction`。要显示 Buff/Reactor 对未来
流程的影响，游戏项目需要在 Expander 或 Adapter 中显式复刻对应规则。

## 不确定性边界

PreviewSystem 不通过反射猜测一段代码是否调用了随机数、玩家输入或网络。领域 `Expander` 在语义边界
显式返回 `SimulationUncertainty`：

```csharp
return new SimulationExpansion<CombatPreviewNode>(
    "选择目标",
    "需要玩家选择另一个目标",
    uncertainty: new SimulationUncertainty(
        SimulationUncertaintyKind.PlayerInput,
        "后续结果取决于玩家选择，预览在此停止。"));
```

内置种类包括随机结果、玩家输入、网络结果、隐藏信息、外部状态和项目自定义。任何随机节点即使拥有
固定种子，也必须返回 `RandomOutcome`，不能向玩家泄露随机结果。鼠标当前悬浮目标已经是已知输入，
不标为 `PlayerInput`；只有未来尚未作出的选择才截断。

## 玩家披露规则

每个一级子节点通过 `PreviewDisclosure` 明确声明玩家应该看到什么：

```csharp
PreviewDisclosure.Trigger("目标获得 2 层燃烧将会触发")
PreviewDisclosure.Trigger("50% 概率追加一次攻击将会触发")
PreviewDisclosure.Trigger("选择另一名目标将会触发")
PreviewDisclosure.NumericChange("伤害减少 50%")
```

`PlayerPreviewFormatter` 只读取根节点的直接子节点：

- `Trigger`：只显示“将会触发”，不显示二级结果；
- `NumericChange`：显示这个一级节点已经确定的直接数值修正；
- 未来玩家输入可以额外显示“等待玩家输入”；
- 随机节点只提示随机效果会触发，不显示抽取结果；
- 未声明 Disclosure 的一级节点安全降级为“{节点名}将会触发”。

卡牌悬浮预览推荐流程：

```text
鼠标进入候选目标
→ 为该目标构造只读战斗快照
→ PreviewPipeline 计算直接伤害，并用 PreviewTrace 列出修正来源
→ SimulationPreview 只构造所有一级子 Action
→ PlayerPreviewFormatter 将普通子 Action 显示为“将会触发”
→ 直接数值修正显示确定效果
→ 随机、未来输入、隐藏信息不追溯后续
```
