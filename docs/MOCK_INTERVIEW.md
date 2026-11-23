# 《勇者远征》模拟面试问答

> 和 [INTERVIEW.md](INTERVIEW.md) 互补：讲解稿按技术点展开，这里按"面试官会怎么问"组织。
> 每题三部分：**答**（30 秒口语）→ **佐证**（代码位置，路径在 `Assets/Scripts/` 下）→ **兜底**（答不深时怎么说）。
> 兜底原则：如实说边界——"这块我是这么做的，原理我知道 X，更深的我没实测过"，不要硬编。

---

## 一、基础

### Q1｜MonoBehaviour 生命周期在项目里怎么用？
- **答**：Awake 做必须最先完成的事：单例去重、抓组件、设刚体参数。Start 做要等别人就绪的事：发初始血量和经验事件给 HUD。OnEnable / OnDisable 成对订阅事件。Update 读输入和做表现，FixedUpdate 改刚体速度，LateUpdate 放相机和程序化形变（要晚于 Animator 求值）。OnDestroy 清单例引用和静态事件订阅。
- **佐证**：`Core/GameManager.cs` Awake/OnDestroy；`Gameplay/HeroController.cs` Update/FixedUpdate；`Gameplay/World/SideCamera.cs` LateUpdate；`Gameplay/ProceduralAnim.cs` LateUpdate。
- **兜底**：脚本执行顺序我只显式设过一处，相机 `[DefaultExecutionOrder(-100)]`，让震屏叠加在相机基准位之后；其他地方靠结构避免顺序依赖。

### Q2｜移动为什么放 FixedUpdate？跳跃输入为什么不放？
- **答**：移动是改 `rb.velocity`，要跟物理固定步长走，放 Update 会因帧率不同手感不同。但"按下"这个事件只在 Update 里可靠，FixedUpdate 可能一帧跑零次或两次，会漏按键。所以 Update 里记录跳跃缓冲计时，FixedUpdate 里消费它。
- **佐证**：`HeroController.Update()` 里 `_jumpBufferTimer`，`FixedUpdate()` 里执行跳跃。
- **兜底**：Fixed Timestep 我没改默认的 0.02s。

### Q3｜土狼时间和跳跃缓冲是什么？
- **答**：土狼时间是走出平台边缘后 0.1 秒内还能起跳，不会因为差一帧而掉下去；跳跃缓冲是落地前 0.12 秒内按跳也算数。再加上下落重力 ×2.5、松键提前下落、顶点重力减小，跳跃才会"脆"而且可控。
- **佐证**：`HeroController` 的 coyoteTime / jumpBufferTime / fallMultiplier / lowJumpMultiplier / apexMultiplier。
- **兜底**：这几个数是参考常见平台游戏的取值再实测调的，没有做系统的手感测试。

### Q4｜Trigger 和实体碰撞体怎么分工？
- **答**：实体碰撞体负责站在地面上、撞墙；Trigger 负责"进入范围就触发"，比如接触伤害、机关、传送门。怪物身上两个都挂，因为两个实体碰撞体之间不会产生 Trigger 事件。代价是范围攻击会扫到同一只怪两次，所以伤害要去重。
- **佐证**：`EditorTools/DemoSceneBuilder.Hero.cs` BuildMonsterBase；`Gameplay/Enemies/EnemyBase.cs` TakeDamage 开头的去重。
- **兜底**：也可以用 `Physics2D.IgnoreLayerCollision` 或把 Trigger 放子物体单独一层，我选了去重是因为改动最小。

### Q5｜ScriptableObject 在项目里做什么？
- **答**：职业配置 `WeaponKit`：攻击间隔、伤害倍率、专属强化池、动画控制器、出招点。切职业就是换一个 Kit。运行时会变的数值放普通类 `HeroStats`，不改 SO 资产。
- **佐证**：`Core/WeaponKit.cs`、`Core/HeroStats.cs`、`HeroController.EquipKit`。
- **兜底**：怪物数值目前直接写在预制体的序列化字段上，没有做成 SO，如果怪物种类再多会改成 SO。

## 二、Animator

### Q6｜三个职业的动画怎么组织？
- **答**：一张基础状态机，三个职业各一个 AnimatorOverrideController，只换剪辑。状态、过渡、参数只维护一份，加职业只需要配帧。怪物也一样，五种怪共用一张 Idle / Move / Windup / Attack。
- **佐证**：`EditorTools/HeroAnimationBuilder.cs` BuildBaseController / BuildOverride；`EditorTools/EnemyAnimationBuilder.cs`。
- **兜底**：没有用 Blend Tree，因为像素帧动画不需要混合。

### Q7｜攻击、受击这种一次性动作怎么切进切出？
- **答**：Any State + Trigger 进入，添加顺序就是优先级：死亡 > 受击 > 攻击；攻击允许打断自己，连击每一下从第一帧开始。播完通过 Exit Time = 1 回到默认态，再由移动层的条件分到 Idle 或 Run。
- **佐证**：`HeroAnimationBuilder` 里的 AnyState / ExitWhenFinished。
- **兜底**：Any State 的坑是条件一直满足时会每帧重进第一帧，所以持续型的前摇用 Bool 并关掉 canTransitionToSelf，一次性的用 Trigger。

### Q8｜你遇到过 Animator 卡住的问题吗？
- **答**：遇到过。攻击动画卡在最后一帧出不来。脚本建的 Exit 过渡默认没有 Exit Time，又没有条件，Unity 会忽略这条过渡。显式打开 Exit Time 就好了。还有一个：死亡时 timeScale 为 0，倒地动画冻在第一帧，把 Animator 的 updateMode 切成 UnscaledTime 解决。
- **佐证**：`HeroAnimationBuilder.ExitWhenFinished`；`HeroController.TakeDamage` 死亡分支。
- **兜底**：—

### Q9｜代码和动画谁负责换图？
- **答**：只有 Animator 写 sprite。HeroController 只写参数，怪物 AI 只在状态切换那一行写 Windup / Attack。两处同时改 sprite 会互相覆盖，而且很难查。
- **佐证**：`HeroController.UpdateAnimatorParams`；`EnemyBase.AnimWindup / AnimAttack`。
- **兜底**：特效（死亡爆散、传送门电光）没有状态切换，用一个很小的计时器组件 `SpriteFx` / `SpriteLoop` 换帧，没用 Animator。

## 三、架构与系统

### Q10｜timeScale 为什么要收口？
- **答**：选职业、升级、暂停、死亡、关卡结算都会冻结时间，如果每个系统自己改 timeScale，A 恢复时可能把 B 的冻结也解掉。所以所有写入都在 GameManager 的状态机里，状态之间互斥。命中停帧是唯一例外，它恢复前会检查当前还是不是 Playing。
- **佐证**：`Core/GameManager.cs`；`Gameplay/Juice.cs` HitStop。
- **兜底**：timeScale = 0 还有一个副作用：`SmoothDamp` 在 deltaTime = 0 时会除零出 NaN，我在相机里踩过，见 Q15。

### Q11｜事件总线有什么风险？
- **答**：静态事件的委托会持有订阅者引用，忘记退订就泄漏，场景卸载后还会调用到已销毁的对象。我的做法是 UI 在 OnEnable / OnDisable 成对订阅，同时 GameManager 销毁时 `ClearAllSubscribers()` 兜底。
- **佐证**：`Core/GameEvents.cs`。
- **兜底**：项目规模小，事件总线够用；更大的项目我会考虑按场景或模块划分的消息系统。

### Q12｜怪物 AI 怎么写的？
- **答**：每种怪是一个枚举状态机，比如梦魇马：走位 → 震地前摇 / 冲锋前摇 → 冲锋 → 撞墙眩晕。所有攻击都有前摇，玩家能看到、能预判。基类负责受击和死亡，子类只写行为。
- **佐证**：`Gameplay/Enemies/EliteGuard.cs`、`BossKing.cs`（三阶段技能表 P1/P2/P3Skills）。
- **兜底**：没有用行为树，因为每种怪只有 3 到 5 个状态，状态机最直接。

### Q13｜打击感做了哪些？
- **答**：命中停帧、受击纯色剪影闪光、视觉层抖动、小怪硬直击退、精英霸体、头顶血条（掉血残影延迟追上）、伤害数字、震屏、死亡爆散序列帧、勇者受击闪白加无敌帧闪烁。
- **佐证**：`Gameplay/Juice.cs`、`SpriteFlash.cs`、`EnemyHealthBar.cs`、`EnemyBase.TakeDamage`。
- **兜底**：音效是程序化合成的，比较简单，这块是短板。

### Q14｜关卡怎么做的？
- **答**：每关是一份 `LevelSpec` 数据：地形段、平台、怪物、机关、目标。编辑器里按它铺碰撞体和像素地块，运行时 `LevelDirector` 按它布置怪物和机关。背景是多层视差，地块用九宫格平铺。
- **佐证**：`EditorTools/LevelBuilder.cs`、`EditorTools/PixelEnvironment.cs`、`Gameplay/World/LevelDirector.cs`。
- **兜底**：没用 Tilemap，因为地形是"高度段 + 平台"这种很规则的结构，数据描述更紧凑；如果要做复杂地形会换 Tilemap。

## 四、调试与 AI

### Q15｜讲一个你最难查的 bug。
- **答**：暂停后相机整关卡死，控制台刷了九百多条 position 是 NaN。顺着调用栈找到 SideCamera 的 SmoothDamp。timeScale = 0 时 deltaTime 是 0，SmoothDamp 在已经到达目标时会算 0 除以 0，速度变量变成 NaN，之后每帧都是 NaN。修法是 deltaTime 为 0 时不推进，并清掉被污染的速度。
- **佐证**：`Gameplay/World/SideCamera.cs` ComputeBasePosition。
- **兜底**：—

### Q16｜你怎么审查 AI 写的代码？
- **答**：两步。一是按清单看：API 版本、生命周期、GC、事件订阅、timeScale、碰撞体数量、写入者是否唯一。二是进游戏验证，不只看能不能编译：慢动作逐帧连拍看动作，用调试指令读血量对伤害。每刀双倍伤害、闪白无效、击退被吃掉，这些都是编译器发现不了、只有验证才能发现的。
- **佐证**：[AI_WORKFLOW.md](AI_WORKFLOW.md) 的七个案例。
- **兜底**：如实说代码初稿大部分是 AI 写的，我的价值在约束、审查和验证。
