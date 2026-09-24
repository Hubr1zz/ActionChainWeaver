namespace CardGame.ActionQueue.Tutorial
{
    internal static class TutorialRuleText
    {
        public static TutorialText BasicLocalized => new(Basic, BasicChinese);
        public static TutorialText ReactorsLocalized => new(Reactors, ReactorsChinese);
        public static TutorialText PresentationLocalized => new(Presentation, PresentationChinese);
        public static TutorialText PreviewLocalized(int bossBlock) => new(Preview + bossBlock, PreviewChinese + bossBlock);

        public const string Basic =
            "GAME ACTIONS\n" +
            "TutorialSequenceAction (Composite root)\n" +
            "  1. Choose target (Command)\n" +
            "  TutorialInnerAction (nested Composite)\n" +
            "    2. Strength roll (Command)\n" +
            "    3. Apply damage (Command, only after a successful roll)\n" +
            "  4. Finish attack (Command, only after inner success)\n\n" +
            "REACTORS\n" +
            "None in this first scene.\n\n" +
            "RELATION\n" +
            "The queue expands each Composite one child at a time. A failed roll ends the inner action; its Failed outcome then propagates to the outer root. No recursive Execute call is used.";

        private const string BasicChinese =
            "游戏行动\n" +
            "TutorialSequenceAction（组合根行动）\n" +
            "  1. 选择目标（命令）\n" +
            "  TutorialInnerAction（嵌套组合行动）\n" +
            "    2. 力量判定（命令）\n" +
            "    3. 造成伤害（仅判定成功后执行）\n" +
            "  4. 完成攻击（仅内层成功后执行）\n\n" +
            "响应器\n" +
            "第一个场景没有响应器。\n\n" +
            "关系\n" +
            "队列每次只展开组合行动的一个子行动。判定失败会结束内层行动，并把 Failed 结果传给外层根行动；执行过程没有递归调用。";

        public const string Reactors =
            "GAME ACTIONS\n" +
            "TutorialAttackAction (Composite) -> Strength check -> Damage\n" +
            "Successful attacks may queue Heal; failed checks may queue Counter Damage.\n\n" +
            "REACTORS\n" +
            "Global: log every resolved Action, regardless of target.\n" +
            "Chain: log only Actions from this one queued attack.\n" +
            "Hero / Source: heal 1 after a successful Attack.\n" +
            "Boss / Target / Before Attack: guard can Prevent the entire attempt.\n" +
            "Boss / Target / Before Damage: reduce damage by 2.\n" +
            "Boss / Target / After failed Check: queue immediate counter.\n" +
            "Local: log only this Attack instance's final outcome.\n\n" +
            "RELATION\n" +
            "Slime has no Boss-target rules. Prevented means no Attack children ran; Failed means the roll ran but failed. The counter is a separate queued Action.";

        private const string ReactorsChinese =
            "游戏行动\n" +
            "TutorialAttackAction（组合）→ 力量判定 → 伤害。\n" +
            "攻击成功可能追加治疗；判定失败可能追加反击伤害。\n\n" +
            "响应器\n" +
            "全局：记录所有已结算行动，与目标无关。\n" +
            "当前链：只记录本次攻击产生的行动。\n" +
            "玩家／来源：攻击成功后治疗 1 点。\n" +
            "首领／目标／攻击前：守卫可使整次攻击无效。\n" +
            "首领／目标／伤害前：伤害减少 2 点。\n" +
            "首领／目标／判定失败后：立即追加反击。\n" +
            "局部：只观察当前攻击实例的最终结果。\n\n" +
            "关系\n" +
            "史莱姆没有首领专属规则。Prevented 表示攻击子行动未运行；Failed 表示判定已经运行但失败。反击是另一个入队的行动。";

        public const string Presentation =
            "GAME ACTIONS\n" +
            "TutorialPresentedDamageAction (Command): subtract HP, publish HitMotion request, optionally await its handle.\n\n" +
            "REACTORS\n" +
            "None. Visual motion is Presentation, not an ActionQueue Reactor.\n\n" +
            "PRESENTATION RELATION\n" +
            "Action -> PresentationDispatcher -> TutorialHitMotionHandler.\n" +
            "Handler stages: Hero moves into foe -> foe flashes -> Hero returns home. Requests share one channel. The upper row shows root Action requests; the lower row shows Presentation requests. IN is submitted since reset; WAIT is still queued.\n" +
            "SKIP WAIT OFF: an Action waits for its motion. SKIP WAIT ON: it resolves while motion continues, equivalent to this demo's former DON'T WAIT attack. INSTANT completes the motion synchronously.\n" +
            "INPUT LOCK cycles OFF -> VISUAL -> LOGIC -> BOTH. VISUAL rejects a new player root while any motion is active or queued. LOGIC rejects it while a root chain is running or queued. INSTANT leaves no active motion, so VISUAL alone does not block. Rejected clicks never change HP or queue counts. Child Actions and Reactor responses bypass this input-only gate.";

        private const string PresentationChinese =
            "游戏行动\n" +
            "TutorialPresentedDamageAction（命令）：扣除生命、发布撞击表现请求，并按设置决定是否等待。\n\n" +
            "响应器\n" +
            "没有。视觉表现属于 Presentation，不属于 ActionQueue 响应器。\n\n" +
            "表现关系\n" +
            "Action → PresentationDispatcher → TutorialHitMotionHandler。\n" +
            "玩家撞向敌人 → 敌人受伤闪烁 → 玩家复位。同一通道的请求依次播放。上排显示根 Action，下排显示表现请求；IN 是重置后的累计提交数，WAIT 是当前排队数。\n" +
            "关闭 SKIP WAIT：Action 等待自己的表现；开启：Action 先结束，表现继续排队。INSTANT 会立即完成表现。\n" +
            "INPUT LOCK 依次切换关闭 → 表现忙 → 逻辑忙 → 两者。表现忙或逻辑忙时可拒绝玩家提交的新根行动；INSTANT 没有持续表现。被拒绝的点击不扣血、不入队；连携子行动和响应器不受输入门影响。";

        public const string Preview =
            "GAME ACTIONS\n" +
            "Dropping Attack creates TutorialDamageAction (Command). It changes Block and HP only when the queue executes it. A surviving Boss may queue Counter Damage.\n\n" +
            "REACTORS\n" +
            "Boss / Target / Before Damage: BlockReactor calculates blocked damage; the Action consumes Block when it executes.\n" +
            "Boss / Target / After successful damaging hit: 50% chance to queue a counter. Slime has neither rule.\n\n" +
            "PREVIEW RELATION\n" +
            "Drag hover -> pure Bonus + Block rules -> first-level preview -> flashing expected HP loss. No GameAction runs and no Block/HP changes.\n" +
            "Drop -> rebuild damage from live buff and target state -> ActionQueue. Drag away -> discard preview. The random counter is disclosed, never predicted.\n\n" +
            "CURRENT BOSS BLOCK: ";

        private const string PreviewChinese =
            "游戏行动\n" +
            "把攻击牌放到目标上会创建 TutorialDamageAction（命令）；只有真正执行时才消耗格挡并扣血。存活的首领可能追加反击伤害。\n\n" +
            "响应器\n" +
            "首领／目标／伤害前：格挡响应器计算被挡伤害；行动执行时才消耗格挡。\n" +
            "首领／目标／成功造成伤害后：有 50% 概率反击。史莱姆没有这些规则。\n\n" +
            "预览关系\n" +
            "拖拽悬停 → 只读的加成与格挡规则 → 一阶预览 → 血条闪烁预计损失。此时不执行 GameAction，也不改变生命或格挡。\n" +
            "松手命中 → 根据最新状态重新计算 → 真正进入 ActionQueue。拖离目标会取消预览。随机反击只提示可能性，不预言结果。\n\n" +
            "当前首领格挡：";
    }
}
