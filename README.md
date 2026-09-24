# ActionChainWeaver

A Unity toolkit for turn-based game flows where one action can trigger many more.

Think of an attack as **choose a target → roll → deal damage**. An enemy might block it, while a successful hit might trigger healing or a counterattack. ActionChainWeaver runs these steps through one queue, so nested effects stay ordered without recursive execution.

## What it includes

- **ActionQueue:** nested actions, clear success/failure/prevention results, and reactions scoped to a character, target, action, or chain. A safety limit warns about runaway effect loops.
- **Presentation:** a separate queue for animations and other visual requests. Game logic can wait for them or continue while they play.
- **Preview:** calculate and show possible results before committing an action, without changing the real game state.
- **Debug window:** inspect the action tree and reactors, or step through the queue one node at a time.

## Try the four scenes

This repository contains Unity assets, **not a complete Unity project**. In a Unity 2022.3 project, install [UniTask](https://github.com/Cysharp/UniTask), enable Unity UI (UGUI), then copy the four top-level folders into `Assets/TurnBasedPack/`. Open `TutorialScenes/Scenes/01_BasicQueue` and press Play. Continue through scenes 02–04 to see reactions, animation queues, and card-drag previews. The demos support **中文 / EN** switching.

For a short tour of each scene, see [TutorialScenes](TutorialScenes/README.md). For implementation details, start with the [ActionQueue guide](ActionQueue/GETTING_STARTED.md). The runtime keeps Unity-specific code in `ActionQueue/Unity`; see [porting notes](ActionQueue/PORTING.md) if you want to adapt the core outside Unity.

---

中文简介：这是一个处理回合制游戏连锁效果的工具。一次攻击可以拆成判定、伤害等步骤；角色状态可以在步骤前后阻止、修改或追加行动。逻辑队列、动画队列和只读预览彼此分开，仓库中有四个可交互的教学场景。
