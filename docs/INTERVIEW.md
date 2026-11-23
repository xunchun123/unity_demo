# 《勇者远征》面试讲解稿

> 口语化，可以直接背。每个技术点按"对应系统 → 关键代码 → 为什么这么做 → 可能的追问"来组织。路径都在 `Assets/Scripts/` 下。

## 一、30 秒项目介绍

> 我做了一个 Unity 2D 横版动作闯关 Demo，叫《勇者远征》。开局三选一职业：长剑、气功、火铳，然后打三关：雾林、熔岩洞窟、古堡内殿，最后一关是三阶段的 BOSS。有二段跳、冲刺、三段连击、怪物出招前摇、升级时的天赋加点和强化三选一，还有死亡结算和暂停。
> 技术上能展开讲的主要有四块：一是用 Animator 状态机加 Override Controller 让三个职业、五种怪物各自共用一张状态机；二是横版平台手感，像土狼时间、跳跃缓冲、可变跳跃高度；三是怪物的模板方法加显式状态机，以及一整套打击感反馈；四是编辑器工具链，动画剪辑、状态机、场景、预制体都由脚本一键生成。
> 开发过程是 AI 辅助写代码，我负责定架构、审查代码和验证，后面也可以讲几个我审查 AI 代码时抓到的真实 bug。

## 二、按 JD 技术点展开

### 1. C# 与面向对象

**对应系统**：怪物体系、伤害管线、职业数据。
**关键代码**：`Gameplay/Enemies/EnemyBase.cs`、`Core/Damage.cs`、`Core/WeaponKit.cs`。

- **模板方法**：`EnemyBase.TakeDamage` 把受击流程固定下来：同帧去重 → 扣血 → 更新血条 → 闪光 → 硬直或霸体 → 击退 → 死亡结算。子类只实现两个抽象方法：`Behaviour()`（每帧 AI）和 `ApplyKnockback()`（飞行怪和地面怪被击退的方式不一样）。新加一种怪，受击表现不会走样。
- **面向接口**：`IDamageable` + `DamageInfo`。`DamageInfo` 是 struct，`TakeDamage(in DamageInfo)` 用 `in` 传参，命中是高频路径，避免分配和拷贝。弹丸只认接口，不认具体是谁。
- **数据和逻辑分开**：职业是 `WeaponKit` ScriptableObject（攻击间隔、伤害倍率、专属强化池、动画控制器、出招点），切职业就是 `HeroController.EquipKit(kit)` 一行。运行时可变的属性放在普通 C# 类 `HeroStats` 里，不直接改 SO 资产，否则会污染下一局。
- **追问：为什么怪物用继承，伤害用接口？** 怪物之间确实共享一大段受击和死亡流程，用抽象基类复用最直接；"能被伤害"是一种能力，勇者和怪物都有，但它们没有共同父类，所以用接口。

### 2. 物理系统：Rigidbody2D 与 Collider2D

**对应系统**：勇者平台移动、怪物碰撞、机关触发。
**关键代码**：`Gameplay/HeroController.cs` 的 `FixedUpdate()`、`IsGrounded()`。

- 移动写在 `FixedUpdate` 里改 `rb.velocity`，跟物理步长走；输入读取（跳跃缓冲、攻击）在 `Update`。
- **手感参数**：土狼时间 0.1s（走出平台边缘还能起跳）、跳跃缓冲 0.12s（落地前按跳也算）、下落重力 ×2.5、上升中松开跳跃键 ×1.6 提前下落、顶点附近重力 ×0.6 悬停一下、二段跳。少了哪一个手感都会发"硬"。
- **碰撞分层**：Player=6、Enemy=7、Ground=8、Projectile=9，用层碰撞矩阵控制谁和谁碰。怪物身上两个碰撞体：一个实体碰撞体负责站在地面上，一个 Trigger 负责接触伤害，因为两个实体碰撞体之间不会产生 Trigger 事件。
- **两个真实的坑**：
  1. 推墙时勇者会粘在墙上半空不掉：墙的静摩擦力和重力平衡了。解决：勇者用零摩擦物理材质。
  2. 空中能无限跳：教学牌、传送门的触发盒放在 Ground 层，地面探测把它们当成了地面，二段跳计数在空中被重置。解决：`IsGrounded` 忽略 Trigger。
- **追问：为什么用 OverlapBox 判地面，不用 OnCollisionStay？** OverlapBox 是主动查询，结果当帧确定，探测盒可以只放在脚底；碰撞回调的顺序和频率不受我控制，侧面贴墙也会进回调。

### 3. Animator：状态机与动画

**对应系统**：勇者三职业、五种怪物。
**关键代码**：`EditorTools/HeroAnimationBuilder.cs`、`EditorTools/EnemyAnimationBuilder.cs`。

- **一张状态机 + Override Controller**：勇者状态 Idle / Run / Jump / Fall / Attack / AirAttack / Hurt / Death，骑士是基础 Controller，猎人、武僧各一个 `AnimatorOverrideController`，只替换剪辑。状态、过渡、参数只维护一份，加第四个职业只需要配一组帧。怪物同理，共用 Idle / Move / Windup / Attack。
- **过渡设计**：地面层 Idle ⇄ Run 按 Speed；离地按 VelY 分 Jump / Fall；Any State 按优先级 Death > Hurt > Attack / AirAttack，攻击允许打断自己（连击每一下都从第一帧挥）。像素帧动画不做混合，过渡时长全是 0。
- **代码和动画的边界**：`HeroController` 只写 Speed / VelY / Grounded / Attack / Hurt / Dead 这几个参数，从来不直接换 sprite；怪物 AI 只在原有的状态切换那一行写 Windup / Attack。前摇动画不循环，停在最后一帧，前摇多长由 AI 的计时器决定，调参时动画不会提前收招。
- **一个真实的坑**：旧版攻击动画播完会卡在最后一帧。原因是脚本里 `AddExitTransition()` 创建的过渡默认 `hasExitTime = false`，又没有任何条件，Unity 会直接忽略这条过渡，状态永远出不去。修法是一次性动作的退出过渡显式打开 Exit Time = 1。
- **死亡动画**：死亡时 GameManager 会把 timeScale 置 0，Animator 默认按缩放时间更新，倒地动作会冻在第一帧，所以死亡时把 Animator 的 updateMode 切成 UnscaledTime。

### 4. UGUI

**对应系统**：HUD、选职业、升级、关卡结算、死亡结算、暂停。
**关键代码**：`UI/*.cs`、`Gameplay/HeroGameBootstrap.cs`。

- **面板显隐由谁驱动**：面板自己不订阅状态事件，由场景引导器 `HeroGameBootstrap` 按 GameState 统一开关。原因是面板初始是隐藏的，隐藏对象的 OnEnable 根本不会执行，自己订阅永远收不到第一次事件。
- **timeScale = 0 时的 UI 动画**：补间、数字滚动全部用 unscaled time。死亡结算面板延迟 1 秒再淡入，先让玩家看清倒地动作；淡入完成前不接受输入，防止死前连按的攻击键直接把结算跳过。
- **中文字体**：TMP 默认字体没有中文，用系统黑体烘焙静态 SDF 图集。静态图集只包含烘焙过的字，改一句文案就可能出现方框，所以我加了一个字表收集器 `GameTextCharset`，搭建时扫描脚本里所有显示给玩家的字符串，缺的字原地补进图集，资产 GUID 不变。
- **追问：HUD 怎么拿数据？** 只订阅 `GameEvents`（血量、蓝量、经验、击杀、关卡阶段、BOSS 血量），玩法层不引用 UI。

### 5. Prefab 与场景管理

**对应系统**：一键搭建、关卡数据、场景流转。
**关键代码**：`EditorTools/DemoSceneBuilder.cs`、`EditorTools/LevelBuilder.cs`、`Gameplay/World/LevelDirector.cs`、`Core/GameManager.cs`。

- **关卡是数据**：`LevelSpec` 描述地形段、悬浮平台、怪物、机关、过关目标。同一份数据在编辑器里用来铺地形和碰撞体，运行时 `LevelDirector` 用来布置怪物和机关，地形和怪物坐标永远对得上。
- **场景流转**：MainMenu → Level1（先进选职业）→ Level2 → Level3。异步加载，状态切换放在加载完成回调里，避免加载过渡帧里旧场景已经恢复成 Playing 开始刷怪。
- **预制体**：勇者、五种怪物、三种弹丸都由脚本生成。物理放根节点，SpriteRenderer + Animator 放 Visual 子物体，程序化形变只作用在 Visual 上，不影响碰撞体。
- **对象池**：`PoolService` 基于 `UnityEngine.Pool.ObjectPool`，预热 + 实例到预制体的反查表；怪物复用时在 `Init` 里调用 `Animator.Rebind()` 复位，上一次可能死在前摇中。

### 6. Git

- 按功能提交，每次提交说明写清楚改了什么、为什么，例如"怪物换像素动画 + 头顶血条 + 受击反馈，修复每刀双倍伤害"。
- `.gitignore` 排除 Library / Temp / Builds、batchmode 日志、系统字体文件、本机开发工具。之前误提交过 81 个日志文件，用 `git rm --cached` 移出了仓库。

### 7. AI 辅助开发与代码审查

详见 [AI_WORKFLOW.md](AI_WORKFLOW.md)。要点：AI 写初稿，我定约束、审查、验证；验证不只是"能编译"，而是进游戏逐帧看动作、用调试指令查血量，下面这些 bug 都是这样抓出来的：

- **每一刀打两次**：怪物有实体和 Trigger 两个碰撞体，范围判定扫到同一只怪两次，伤害翻倍。修法是 `EnemyBase` 里同帧、同来源、同数值的伤害去重。
- **闪白是无效的**：`sr.color = Color.white` 是乘法染色，白色乘原图还是原图。改成叠一层用 GUI/Text Shader 的纯色剪影（只用贴图 alpha）。
- **击退被吃掉**：怪物 AI 每帧重写速度，击退刚推出去就被覆盖。加了 0.22 秒受击硬直，期间 AI 暂停。
- **相机卡死**：暂停或选职业时 timeScale = 0，`Mathf.SmoothDamp` 在 deltaTime = 0 且已到达目标时会算出 0/0，速度变量变成 NaN，之后整关相机都动不了。修法是 deltaTime 为 0 时不推进，并清掉被污染的速度。

## 三、最可能被问到的三个问题

**Q：这个项目里你自己做了什么，AI 做了什么？**
> 代码初稿大部分是 AI 写的。我做的是：定架构和约束（比如 timeScale 只许 GameManager 改、Animator 是 sprite 唯一写入者）、选素材和定画风、审查每一份代码、进游戏验证并定位问题。上面那几个 bug，AI 写的时候都没发现，是我在验证时抓到、分析出原因再让它改的。

**Q：如果要加一个新怪物，要改哪些地方？**
> 三步：准备像素帧放到 `Art/Pixel/新怪物名/`；在 `EnemyAnimationBuilder` 里加一组剪辑配置（复用同一张状态机）；写一个继承 `EnemyBase` 的 AI 类，实现 `Behaviour` 和 `ApplyKnockback`，在状态切换处写 Windup / Attack 参数。受击、血条、死亡特效都是基类自带的。

**Q：性能上做了什么？**
> 弹丸、伤害数字走对象池；物理查询只在需要时做（攻击那一帧、固定间隔的落脚点采样）；像素图不压缩但尺寸都很小。说实话这个体量还没碰到性能瓶颈，我没有做过 Profiler 层面的深入优化，这点我会如实说。
