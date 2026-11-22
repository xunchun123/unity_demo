# 《星涡幸存者》模拟面试问答库（32 问 · 三档难度）

> 与 `INTERVIEW.md`（讲解稿，按 JD 技术点系统展开）互补：本文按"面试官怎么问"组织，不重复讲解稿结构。
> 每问三件套：30 秒口语参考答案 → 代码佐证（路径均指 `scripts/` 下源码）→ 答不上来的兜底话术。
> 兜底话术统一原则：诚实交代边界——"这块我目前是这么做的，原理层面我知道 X，再深我没实测过"，绝不硬编。
> 使用建议：先背答案第一句和佐证，兜底话术原样记住——面试里"承认边界"比"编"加分得多。

---

## 一、基础档（Q01–Q12 · 对应 JD 的 8 个技术点）

### Q01｜Unity 的 MonoBehaviour 生命周期，你在项目里是怎么实际使用的？

- **答（30秒）**：我不背定义，直接按项目讲每个回调各司其职。Awake 只做"必须先于别人"的事：单例去重、DontDestroyOnLoad、组件兜底抓取、刚体参数（重力 0、连续碰撞检测）；Start 做"要等其他对象就绪"的事：玩家 Start 里创建 PlayerStats、发初始 HP/经验事件、装备武器；OnEnable/OnDisable 严格成对，做事件订阅和池对象计数；Update 管表现与节流，FixedUpdate 管物理移动，LateUpdate 管相机（必须在所有位移算完之后）；OnDestroy 清单例引用和事件订阅。
- **佐证**：`Core/GameManager.cs` · `Awake/OnDestroy`；`Gameplay/PlayerController.cs` · `Awake/Start`；`Gameplay/Enemy.cs` · `OnEnable/OnDisable`（AliveCount 计数）；`Gameplay/CameraRig.cs` · `LateUpdate`
- **兜底**：执行顺序我答得出——同帧所有对象 Awake 先跑完再跑 OnEnable；再深到 ScriptExecutionOrder 我了解机制，但项目没显式设过，靠结构避免顺序依赖。

### Q02｜Update 和 FixedUpdate 怎么分工？玩家移动为什么放 FixedUpdate？

- **答（30秒）**：一句话标准——"跟物理积分走的数据写 FixedUpdate，跟帧率和表现走的写 Update"。移动放 FixedUpdate 是因为写的是 `rb.velocity`，速度要被物理引擎按固定步长（默认 0.02s）积分；放 Update 写的话帧率高时同一物理步被覆盖、帧率低时物理步拿不到新输入，不同帧率下手感不一致。Update 里放的是无敌帧闪烁、回血、拾取圈半径同步这类表现逻辑。另外两处都有状态守卫：非 Playing 态 FixedUpdate 直接把速度清零，保证暂停刹得住。
- **佐证**：`Gameplay/PlayerController.cs` · `FixedUpdate()`（守卫 + `_rb.velocity = ReadMoveInput() * Stats.MoveSpeed`）、`Update()`；`Gameplay/Enemy.cs` · `FixedUpdate()`
- **兜底**：追问间隔能不能改——Project Settings→Time→Fixed Timestep，调大省 CPU 但物理更糙；我没动默认值，同屏 90 怪实测够用。

### Q03｜Rigidbody2D 改 velocity 和直接改 Transform 有什么区别？你为什么选前者？

- **答（30秒）**：改 velocity 是"把意图交给物理引擎"——碰撞、插值、CCD 都在物理管线里一致生效；改 Transform 是"绕过物理"——引擎不知道你动了，高速会穿透、跟随插值会抖。我是顶视角恒速直控，velocity 最贴：响应快、不用积累力。配套两个细节：玩家和弹丸都开 `CollisionDetectionMode2D.Continuous` 防高速穿透；暂停态统一清零速度，防止残留速度滑行。
- **佐证**：`Gameplay/PlayerController.cs` · `Awake()`（gravityScale=0、Continuous）、`FixedUpdate()`；`EditorTools/DemoSceneBuilder.Prefabs.cs` · `BuildProjectilePrefab`（弹丸同样 Continuous）
- **兜底**：MovePosition/AddForce 选型我答得清——MovePosition 适合运动学受控移动、AddForce 适合外力积累（击退），本场景恒速直控 velocity 合理；AddForce 的 forceMode 细节我了解原理、项目没用到。

### Q04｜Collider2D 的触发器和实体碰撞怎么选？你的弹丸、敌人、宝石各用的哪种，为什么？

- **答（30秒）**：按真实代码讲是三层分工。弹丸用 Trigger：命中判定不该阻挡任何人，碰到就结算再回池。敌人也用 Trigger，但原因不同——我不想让物理求解器互推几百只怪（费 CPU 且表现失控），间距交给分离力算法管，层碰撞矩阵里干脆把 Enemy↔Enemy 关了。玩家本体是实体圆碰撞：它是触发对里带刚体的一方，检测由它承担。宝石最特殊：纯触发器且不带 Rigidbody2D——触发回调只要对里有刚体就触发，玩家侧有刚体，几百颗宝石每颗省一个刚体。接触伤害走 `Enemy.OnTriggerStay2D`，内部 contactCooldown 冷却加玩家 0.6s 无敌帧双保险防同帧多次扣血。
- **佐证**：`Gameplay/Projectile.cs` · `OnTriggerEnter2D`；`Gameplay/Enemy.cs` · `OnTriggerStay2D`；`Gameplay/XPGem.cs`（宝石无刚体）；`EditorTools/DemoSceneBuilder.Settings.cs` · `SetupPhysicsMatrix`（三条碰撞对）；`EditorTools/DemoSceneBuilder.Prefabs.cs` · `BuildEnemyPrefab` 第 180 行（isTrigger 注释"不参与物理互推，间距由分离力管"）
- **兜底**：触发器不进求解器约束、只做检测，便宜——这个我答得清；再深到 Box2D 内部 island 求解我了解概念、没读过源码。

### Q05｜你的 UI 是怎么和玩法层解耦的？事件系统具体怎么工作？

- **答（30秒）**：静态 C# event 总线。玩法层只发布——发布方法用 internal 修饰，只有 Core 程序集能发；UI 只订阅——HUD 订阅 PlayerHpChanged/XpChanged 等，数值变化才刷新，不做每帧轮询；面板显隐的单一事实来源是 GameStateChanged。依赖方向反转后，玩家/敌人/弹丸这些核心玩法类对 UI 零依赖；唯一的接线点集中在一个装配器 GameSceneBootstrap，它显式注入面板引用——依赖收敛到一个装配位置而不是散落各处。
- **佐证**：`Core/GameEvents.cs`（10 个事件、internal Publish、ClearAllSubscribers）；`UI/HUDController.cs` · `OnEnable/OnDisable`（订阅对称）；`Gameplay/GameSceneBootstrap.cs` · `OnStateChanged`（面板显隐由引导器驱动）
- **兜底**：为什么不用 UnityEvent 或 SO 事件通道——demo 规模下静态 event 零分配零资产，规模变大升级到 SO 通道的路径明确，这是按规模选型不是偷懒。

### Q06｜ScriptableObject 是什么？你为什么这么用？运行时改数据怎么办？

- **答（30秒）**：SO 是 Unity 的数据容器资产，存在 Assets 里、所有引用共享同一份。我拿它当"策划配置"：EnemyData 三份资产对应三种怪，WeaponData/UpgradeData/DifficultyCurve 全是纯配置，改平衡不动代码不重编译。关键纪律：SO 是模板不是存档——运行时可变数据（等级、暴击、倍率）放普通 C# 类 PlayerStats，从模板拷一份出来改；绝不改 SO 资产本身，否则改动在编辑器里持久化，污染之后所有对局。
- **佐证**：`Data/EnemyData.cs`、`Data/WeaponData.cs`、`Data/DifficultyCurve.cs`；`Gameplay/PlayerStats.cs`（类头注释"运行时数据与配置数据分离"）
- **兜底**：SO 编辑器下改动持久化这个坑我踩过也答得清；再深到 SO 与 Addressables 的引用计数配合，我没实操过。

### Q07｜Animator 状态机是怎么驱动的？参数怎么设的？

- **答（30秒）**：玩家 Idle/Run 两态，布尔参数 Moving 驱动，过渡时长 0.1s；三种敌人各一个单状态 Walk 循环（循环态无需参数）。控制器不是手摆的——`DemoSceneBuilder.CreateAnimators` 用 Editor API 生成：AddState/AddTransition 搭结构，序列帧用 ObjectReferenceKeyframe 烘成 AnimationClip，末帧补回首帧保证循环等时长。这里我主动交代一个复盘发现的缺口：参数和过渡都配好了，但运行时我漏了在 PlayerController 里 SetBool("Moving", ...)——目前玩家一直播 Idle，Run 态切不进去；敌人侧动画链路是通的。正确修法一行代码，这条在我自己审查清单上——正好说明"配置完整不等于链路通"。
- **佐证**：`EditorTools/DemoSceneBuilder.Anim.cs` · `CreateAnimators/MakeClip`（Moving 参数、0.1s 过渡、末帧回首帧）；缺口佐证：`Gameplay/PlayerController.cs` 全文无 SetBool 调用
- **兜底**：如果被问"那动画不是白配了"，我答敌人动画正常工作、玩家侧是我明确知道的缺口和修法——宁可主动交代也不等被抓。

### Q08｜场景切换是怎么管理的？跨场景对象怎么办？

- **答（30秒）**：两个场景 MainMenu/GameScene，切换收口在 GameManager：StartNewRun/BackToMenu 用 LoadSceneAsync，同时把状态切到位、timeScale 拉回 1——先复位状态再加载，新场景组件按状态初始化。跨场景对象是四类服务单例（GameManager/PoolService/AudioService/Juice）DontDestroyOnLoad 常驻；场景内对象每局重建。新场景的运行时装配交给每场景一个引导器：Start 里做"必须等场景跑起来才能做"的事——相机跟随目标注入、面板按当前状态设初始显隐、再订阅后续状态切换。
- **佐证**：`Core/GameManager.cs` · `StartNewRun/BackToMenu`；`Gameplay/GameSceneBootstrap.cs` · `Start()`；四服务 `DontDestroyOnLoad`（各自 Awake）
- **兜底**：demo 没做 loading 屏，LoadSceneAsync 直接挂；要做的话 allowSceneActivation 分两步配合进度条，我了解做法、没实现过。

### Q09｜UGUI 多分辨率适配怎么做的？Canvas Scaler 参数是什么？

- **答（30秒）**：Canvas Scaler 用 ScaleWithScreenSize，参考分辨率 1920×1080，screenMatchMode 取 MatchWidthOrHeight、matchWidthOrHeight = 0.5——宽高各占一半权重，宽高比从 16:9 到 21:9 变化时 UI 缩放取折中，不会一边过大一边过小。元素本身靠锚点定位，变化量交给锚点。飘字这类跟世界坐标走的 UI 是独立 world space Canvas，不参与屏幕适配。
- **佐证**：`EditorTools/DemoSceneBuilder.UI.cs` 第 24–28 行（Scaler 参数）；`Gameplay/DamagePop.cs`（世界空间 TMP 飘字）
- **兜底**：0.5 是经验值——UI 以居中为主、边缘 HUD 少；极端竖屏要按锚点重新布局，我了解 Match 取 0/1 的极端策略但没针对性做过。

### Q10｜Prefab 实例化和对象池是什么关系？Instantiate 到底慢在哪？

- **答（30秒）**：Prefab 是模板，Instantiate 是"深拷贝到场景"——对象图遍历、组件构造、序列化反序列化都发生在这，单次毫秒级，一帧生成几十个就是尖峰。池化不消灭 Instantiate，它把时机从"玩法进行中"挪到"加载期"：Register 时预热水位（先 Get 一轮再 Release 回去，成本进场景前付掉），运行中 Spawn/Despawn 只剩 SetActive 加位置设置。maxSize 管闲时内存：闲置超量就销毁。
- **佐证**：`Core/PoolService.cs` · `Register`（第 69–71 行预热循环）、`Spawn/Despawn`；池对象统一挂 `~Pools` 节点
- **兜底**：Instantiate 慢我能答到"对象图遍历 + 组件 Awake/OnEnable 链"层面；再深到序列化系统源码层我了解机制、没剖析过。

### Q11｜伤害管线是怎么设计的？接口和 struct 有什么讲究？

- **答（30秒）**：窄管线：DamageInfo 是 struct（数值/来源位置/是否暴击），`TakeDamage(in DamageInfo)` 用 in 修饰——每颗子弹每次命中都走这条路径，struct 避免堆分配 GC，in 避免传参整体拷贝。受击方只实现 IDamageable 接口，弹丸命中时 `GetComponentInParent<IDamageable>()`——它不知道打的是敌人还是别的，依赖倒置，新增受击物不改弹丸一行。暴击在发射方 roll，数值沿管线传下去。
- **佐证**：`Core/Damage.cs`（struct + 接口）；`Gameplay/Projectile.cs` · `OnTriggerEnter2D`；`Gameplay/WeaponController.cs` · `SpawnProjectile`（暴击 roll）
- **兜底**：装箱陷阱我答得出——DamageInfo 不实现接口不装箱；但通过接口调用 in 参数方法会触发防御性拷贝这个细节我也知道；再深到 JIT 行为我了解结论、没验证过。

### Q12｜输入是怎么处理的？为什么用新版 Input System 的轮询？

- **答（30秒）**：设备轮询：`Keyboard.current.wKey.isPressed` 读持续状态，`wasPressedThisFrame` 读边沿。持续移动的读取放 FixedUpdate（轮询当前状态天然安全）；边沿事件（Esc/P 暂停、三选一数字键 1/2/3）放 Update。手柄左摇杆和键盘相加后归一化，防斜向 1.414 倍速。为什么轮询不用 Action 事件：割草移动每物理帧都要读，轮询零委托开销、路径直白；离散交互才适合事件回调。
- **佐证**：`Gameplay/PlayerController.cs` · `ReadMoveInput()`；`Core/GameManager.cs` · `InputEscapePressed`；`UI/LevelUpPanel.cs` · `Update()`（数字键）
- **兜底**：Action Map/PlayerInput 组件我答得出概念和适用场景（重绑定、多设备管理该上）；重绑定 UI 我没实际配过。

---

## 二、进阶档（Q13–Q24 · 面试官往深挖一层，追问式）

### Q13｜对象池 maxSize 满了会怎样？池空了 Get 会怎样？

- **答**：分两头说。Get 时池空：不报错，直接调 createFunc 新建——所以 maxSize 不是硬上限，运行峰值可以超。Release 时闲置队列已满：新还回来的实例立刻调 actionOnDestroy 销毁——不是拒绝归还，是"收下就拆"。合起来：峰值期动态扩容，退潮期超额闲置对象被回收，池规模自动收敛回 maxSize。项目里敌人池 maxSize 160、同屏上限 90，参数是配套定的。
- **佐证**：`Core/PoolService.cs` · `Register`（maxSize 传给 ObjectPool）；`Gameplay/EnemyDirector.cs` · `Start`（prewarm 32 / maxSize 160）
- **再追问会到哪**：defaultCapacity 和 maxSize 的区别（前者只是初始 List 容量提示，不封顶）；怎么可视化观察池水位（我没做，思路是调试面板显示闲置计数）。
- **兜底**：源码级行为（闲置列表计数、销毁回调）我读过官方实现确认；"归还后仍持有引用导致复用冲突"是真实风险，我的纪律是"归还不持有"。

### Q14｜collectionCheck 为什么只在 Editor 开？关了怎么保证不出 bug？

- **答**：它防"双重归还"——同一实例 Release 两次。开着时 Release 会线性扫一遍闲置列表，发现重复抛异常，代价 O(闲置数量)；发布版每次归还都扫太浪费。我敢关是因为所有归还收敛在 PoolService.Despawn 单一入口，且归还必须先过反查表——double-release 的路径在结构上很难构造出来。这是"用代码纪律换运行时开销"的典型取舍。
- **佐证**：`Core/PoolService.cs` 第 64 行 `collectionCheck: Application.isEditor`；单一归还入口 `Despawn`
- **再追问会到哪**：双重归还真发生了会怎样——同一实例进两次闲置队列，下次 Get 发出两个同物体引用，一个改状态另一个不知情，典型表现为"幽灵子弹"；官方检查还有 SceneCheck/AlwaysCheck 两档的区别。
- **兜底**：官方源码行为我读过；生产项目要不要关，我会先 Profile 确认 Release 不在热路径再决定，不拍脑袋。

### Q15｜池对象状态重置漏了会出什么 bug？拿你的 Enemy.Init 举例。

- **答**：池对象是"二手机"，Init 漏一项就是一个偶发诡异 bug。我 Init 里重置的清单：血量（不重置则复用出一只残血怪，秒不掉）、接触冷却计时器（不重置落地瞬间就能咬人）、分离节流相位（随机错峰）、localScale（不重置则精英的 1.4 倍体型穿给下一只普通怪）、色调（精英提亮色残留）。OnEnable 侧也有：Projectile 的 _lifetime 归零、XPGem 的磁吸初速和浮动相位。原则：重置收口在取出路径（Init/OnEnable），别依赖归还路径帮你清——归还路径永远会被新代码绕过。坦白一个隐患：XPGem 的 _attracted 标志不在 OnEnable 重置，靠 Collect 和 OnTriggerExit 顺手清；今天唯一归还路径是 Collect 所以安全，但哪天加"清场直接 Despawn"，复用的宝石会带着磁吸状态乱飞——正确做法是把 `_attracted = false` 挪进 OnEnable。
- **佐证**：`Gameplay/Enemy.cs` · `Init`（逐行重置清单）；`Gameplay/Projectile.cs` · `OnEnable`；`Gameplay/XPGem.cs` · `OnEnable`（及 _attracted 隐患）
- **再追问会到哪**：为什么不用 IPoolable 接口强制所有池对象实现 OnSpawn/OnDespawn——通用池是 GameObject 级的，接口侵入所有组件；规模大了值得上。
- **兜底**：这个隐患是我复盘时发现的，正好当案例讲——说明状态重置必须收口、不能散。

### Q16｜timeScale = 0 的时候，哪些东西还在跑？

- **答**：冻结的是"缩放时间"：Time.deltaTime 归零、物理步进停（FixedUpdate 不再被调）、默认 Animator 和粒子冻结、WaitForSeconds 永远等不到。但 Update 和协程照跑、unscaled 时间照流。所以我的体系分两类：想动的走 unscaled——Tween 默认 unscaled（升级卡片弹入、结算数字滚动）、HUD 血条残影用 unscaledDeltaTime（死亡瞬间 timeScale=0 血条照样流完）、HitStop 计时用 WaitForSecondsRealtime（停帧期间不被自己冻结）、结算 0.9s 延迟也是 Realtime；想冻的走 scaled——怪物/宝石/飘字停在原地（它们的 Update 还在跑，但 delta 是 0，自然冻结）。还有个推论：LevelUpPanel 的键盘 1/2/3 可用，正是因为暂停态 Update 照跑。
- **佐证**：`Core/Tween.cs`（unscaled 默认 true）；`UI/HUDController.cs` · `Update`（unscaledDeltaTime）；`UI/GameOverPanel.cs` · `ShowRoutine`；`Gameplay/Juice.cs` · `HitStopRoutine`
- **再追问会到哪**：暂停时让粒子继续放（ParticleSystem.MainModule.useUnscaledTime）；Animator 的 updateMode 三档（Normal/AnimatePhysics/UnscaledTime）。
- **兜底**：这些行为我查文档加实测确认过；协程调度器内部实现我没读源码，但"协程恢复发生在 Update 循环里、不受 timeScale 影响本体"这个结论我有把握。

### Q17｜HitStop 和玩家暂停同时发生怎么办？

- **答**：timeScale 有四个来源（升级/暂停/死亡/HitStop），我收口成"状态机写常规值，HitStop 是唯一带协议的例外"。协议三层：① 入口守卫——State 不是 Playing 直接不生效，暂停态触发不了停帧；② 取 max 不叠加——停帧中再触发只更新 pending 时长，不重启协程；③ 恢复前再查一次状态——核心竞态：停帧进行中玩家按 Esc，状态机把 timeScale 改成 0，HitStop 协程结束时如果无脑还原备份的 0.05，暂停就被破坏；再查 State 仍是 Playing 才还原，不是就放弃。终值永远归状态机管，验收标准就是"任意顺序触发，timeScale 终值等于状态机预期"。
- **佐证**：`Gameplay/Juice.cs` · `HitStop/HitStopRoutine`（三处守卫）；`Core/GameManager.cs` · `RequestLevelUps`（死亡瞬间同帧升级被 State 守卫拦下）
- **再追问会到哪**：为什么不用引用计数管 timeScale——计数能解冲突但表达不了"升级时不能暂停"这类互斥语义，状态机还顺便给了 UI 单一事实来源；多个 HitStop 源要不要分级（弱击 0.02 强击 0.08，取 max 就够）。
- **兜底**：这套协议是先画时序推演再实现的；"停帧中死亡"分支我推演过——HandlePlayerDeath 有 GameOver 守卫，HitStop 恢复查 State 非 Playing 不还原，正确。

### Q18｜静态事件的内存泄漏，具体是什么场景？你怎么防的？

- **答**：机制：静态事件的委托链持有订阅者引用，订阅者不退订就永远到不了 GC——两层后果：内存不回收，以及事件再触发时回调打到已销毁的 Unity 对象上抛 MissingReferenceException。经典时序：场景 A 的面板订阅总线 → 切场景时面板销毁但没退订 → 回菜单一发事件就炸。我两层防护：常规路径，UI 面板全部 OnEnable 订阅/OnDisable 退订严格对称；兜底路径，GameManager.OnDestroy 里 ClearAllSubscribers 全量清空——场景常驻者退场时总线归零，"没来得及退订"的对象跟着一起走。另外指定唯一负责人：清空只发生在 GameManager，多系统各自清会互相踩。
- **佐证**：`Core/GameEvents.cs` · `ClearAllSubscribers`；`Core/GameManager.cs` · `OnDestroy`；`UI/HUDController.cs`（对称订阅范例）
- **再追问会到哪**：Enter Play Mode Options 关域重载时静态状态跨 Play 残留（用 [RuntimeInitializeOnLoadMethod] 归零）；lambda 直接订阅无法退订的坑（必须保存委托引用）。
- **兜底**：泄漏的验证思路我懂（反复进出场景看内存是否阶梯上升）；Memory Profiler 的快照 diff 我会操作，但没在这个项目上系统跑过。

### Q19｜MaterialPropertyBlock 为什么不会实例化材质？你的项目哪里能对比说明？

- **答**：直接写 `sr.material.color` 时，material getter 第一次访问就会实例化一份材质副本——每个池对象一份，内存涨、批处理关系被打断。MaterialPropertyBlock 是"渲染器级的属性覆盖"，数据进块缓冲区，不碰共享材质资产。Juice.Flash 里 GetPropertyBlock→SetColor(_BaseColor)→SetPropertyBlock，60ms 后 Clear 还原，全程零实例化，池对象反复复用同一份块。反面对比更有意思：BackgroundParallax 是项目里唯一故意 new Material 的地方——视差层需要"原图 + Repeat 包裹 + UV 偏移"，进图集 UV 会切到邻居精灵，材质必须独立实例。一个是"绝不实例化"、一个是"必须实例化"，同一段代码库里正好是取舍的两面。
- **佐证**：`Gameplay/Juice.cs` · `FlashRoutine`（BaseColorId + MPB）；`Gameplay/BackgroundParallax.cs` · `Start`（刻意实例化及注释）
- **再追问会到哪**：SRP Batcher 与 MPB 的关系——SRP Batcher 靠共享材质数据合批，用 MPB 的渲染器会退出 SRP 批处理，这是 2022 里真实的取舍点（实例少时 MPB 的省内存收益仍可能大于合批损失）。
- **兜底**：MPB 不实例化材质是文档明确的行为；SRP Batcher 冲突细节我了解原理，没有实测对比数据，这个我如实说。

### Q20｜把刷怪节奏做成 AnimationCurve，实际好处是什么？不是多此一举吗？

- **答**：三个实际好处。① 调参不动代码：刷怪速率、怪种权重、血量成长、精英概率都是曲线，拖关键帧就行，不重编译不重启；② 时间维度天然建模：这些数值本质是"关于分钟数的函数"，曲线比 if-else 链更准确还自带可视化——darter 第 2 分钟"渐入"写成三个关键帧，曲线插值给出平滑过渡，怪种出现不是硬切；③ Evaluate 是 O(1)，SpawnOne 每次刷怪调四次 Evaluate，权重数组缓存在字段里主循环零分配。
- **佐证**：`Data/DifficultyCurve.cs`（四条曲线定义）；`Gameplay/EnemyDirector.cs` · `SpawnOne`（权重采样 + `_weights` 缓存）
- **再追问会到哪**：曲线多了以后怎么办——SO 只存结构、真实数值走表管线（Excel 导入工具生成 SO），这是成熟项目的形态。
- **兜底**：Evaluate 的插值模式我调过；曲线资产在 git diff 里是文本可读的，这点我真验证过——这也是资产代码化的副产品。

### Q21｜Physics2D.OverlapCircleAll 的性能特征是什么？你做了哪些节流？

- **答**：三条特征：① 每次调用分配一个结果数组（托管 GC 垃圾），这是它和 NonAlloc/缓冲区重载的核心差异；② 查询成本和"命中数量"成正比而不是和场景大小成正比，层掩码在 broadphase 就过滤；③ 频率是放大器——同屏 90 只怪每帧全查就是每秒几千次。我的削减组合：索敌 0.1s 节流加缓存目标、分离力 0.15s 节流加结果最多采样 6 个邻居就截断、每只怪节流计时器随机相位错峰（避免 90 只同帧集体 Overlap）、距离比较用 sqrMagnitude 不开方、同屏上限 90 是总闸。真到 GC 压力 Profile 出问题，下一步换 NonAlloc/List 缓冲区重载把分配归零——我留了这个演进口。
- **佐证**：`Gameplay/Enemy.cs` · `ComputeSeparation`（采样截断 + 相位错峰）；`Gameplay/WeaponController.cs` · `FindNearestEnemy`（0.1s 节流 + sqrMagnitude）；`Gameplay/EnemyDirector.cs` · `Update`（超限跳帧）
- **再追问会到哪**：空间划分（均匀网格分桶）什么时候值得上——敌人数量再上一个数量级、或查询半径远大于邻居间距时；我了解写法，demo 规模下查询已不是瓶颈就不上。
- **兜底**：分配量级是按调用频率×数组大小估算的，Deep Profile 验证 GC 这步我没做，敢说是因为节流后频率低到可忽略。

### Q22｜震屏的 trauma 模型，为什么抖动量要用 trauma 的平方？

- **答**：平方解决的是"量的控制"这个真问题。trauma 线性累积、Clamp01 封顶，实际抖动量 = trauma²。效果一：小抖不扰民——击杀单怪只加 0.07，平方后 0.005 几乎无感；玩家受伤 0.35 平方后 0.12 明显、精英死亡 0.22 平方后 0.05 中等——事件权重天然分层，不用逐档调参数。效果二：衰减观感——trauma 每秒线性衰减 1.6，抖动量按平方衰减意味着前段剧烈后段柔和收尾；直接线性衰减抖动量的话，尾巴会一直残余小幅晃动，像相机没站稳。效果三：连续受击叠加时 Clamp01 保证封顶，不会把玩家晃吐。噪声用 Perlin（25Hz 采样、三通道不同坐标防同步位移旋转同步），随机数抖动观感尖锐毛躁。这套模型出处是 GDC 的相机数学分享（Squirrel Eiserloh），我按自己的参数落地。
- **佐证**：`Gameplay/CameraRig.cs` · `LateUpdate`（`shake = _trauma * _trauma`、衰减 1.6/s、AddTrauma Clamp01）；`Gameplay/PlayerController.cs` · `TakeDamage`（受伤 0.35）
- **再追问会到哪**：位移抖动和旋转抖动的感知差异（小角度旋转比位移更"真实"，大位移会晕）；为什么恢复不用 easeout 手调（衰减模型已经给出自然收尾）。
- **兜底**：参数 0.35/0.22/0.07 是按"事件越重反馈越重"手调的；Perlin 频率 25Hz 调过两档，再细的频谱分析没做。

### Q23｜对象池反查表（实例→预制体）这个设计，权衡是什么？

- **答**：起点是 API 责任：官方 ObjectPool.Release 要求调用方持有池引用，我的验收标准是"归还一行调用"——Despawn(gameObject) 只拿实例，不知道自己是谁生的。反查表让 Spawn 时登记 实例→预制体、Despawn 时反查归属池。收益：调用方零心智负担，"传错池归还"这类 bug 结构上不可能发生——Projectile.ReturnToPool 真的只有一行。代价：每个活跃实例一条字典项，但键是复用中的实例、数量被 maxSize 封顶，字典不膨胀。考虑过的替代方案：让池对象实现 IPoolable 自己记住来源——要侵入每个组件，通用 GameObject 池不划算。还有一个纪律性风险要主动交代：绕过 Despawn 直接 Destroy 池对象，反查表会留下死键持有已销毁引用——当前所有归还路径收敛在 Despawn，靠纪律守住；要工程化就加周期清理或挂 OnDisable 检查。
- **佐证**：`Core/PoolService.cs` · `_instanceToPrefab`（字段注释"键数量稳定不膨胀"）、`Spawn`（登记）/`Despawn`（反查 + 未注册兜底 Destroy）
- **再追问会到哪**：为什么用 GameObject 当字典键（引用比较、GetHashCode 是实例 ID，稳定）；未注册实例兜底 Destroy 存在的原因（调用方不用关心对象从哪来）。
- **兜底**：膨胀与否我推演过数学（键数 = 活跃数 ≤ maxSize）；没有实测内存数字，如实说。

### Q24｜中文字体 TMP SDF 烘焙的原理是什么？为什么系统字体不能直接用于 TMP？

- **答**：从"为什么需要"讲。TMP 不直接吃系统字体文件：它需要一份 FontAsset——字形位图图集加每个字符的度量数据。默认字体 Liberation Sans 没有 CJK 字形，中文直接显示方块。SDF 的意思是：烘焙时把每个字符渲染成"到字形边缘的有符号距离图"，运行时 shader 按距离重建边缘——好处是任意缩放边缘都锐利（普通位图字体放大就糊）。系统字体不能直接用有两层：一是缺 FontAsset 封装，二是就算做成动态字体模式，首次遇到新字符要运行时扩图集（烘焙加重排，明显卡顿），打包后还依赖目标机装没装这个字体。我的方案：编辑器期从微软雅黑建 FontAsset（72pt 采样、padding 6、SDFAA、1024² 图集、开多页），先切 Dynamic 模式把项目全部 UI 字符串一次 TryAddCharacters 烘进图集，然后定格回 Static——打包自包含，运行时零扩容。
- **佐证**：`EditorTools/DemoSceneBuilder.cs` · `EnsureCjkFont`（CreateFontAsset 参数、Dynamic 烘焙后切 Static、设 TMP_Settings.defaultFontAsset）；字符集来源 `DemoSceneBuilder.Scenes.cs` 的 ChineseChars
- **再追问会到哪**：玩家昵称这类开放字符集——静态为主加动态白名单兜底（fallbackFontAssetTable 挂动态资产），常见生产组合；图集多页对 drawcall 的影响。
- **兜底**：单通道 SDF 和多通道 SDF 的边缘质量差异我了解概念，项目用的内置 SDFAA；手写距离场烘焙我没做过。

---

## 三、开放档（Q25–Q32 · 设计思维与工程素养，答案要有观点和取舍）

### Q25｜如果让你给这个游戏加一个 Boss 战，你会怎么设计？

- **答**：我会把 Boss 当成"波次导演的插播事件"而不是新系统，选最小改动面积。数据侧：BossData SO 复用 EnemyData 的公共字段（血量/接触伤害/经验），加阶段表——每阶段一个行为块（移动模式、攻击模式、血量阈值），行为块也做成数据，坚持零代码加 Boss。导演侧：DifficultyCurve 加一条 bossSchedule（第几分钟插哪个 Boss），EnemyDirector 到点置 bossActive 暂停普通刷怪，Boss 死亡广播事件、恢复曲线进度。表现侧全部复用现成管线：大数值 HitStop、CameraRig.Kick 定向冲击、DeathBurst 大爆点、GameEvents 加 BossHpChanged 给 HUD 血条。和状态机的冲突预先想好：Boss 战期间玩家升级弹窗照常允许（timeScale=0 全场冻结，Boss 演出协程用 Realtime 计时不受影响），绝不做"Boss 强制打断升级"这种和状态机抢语义的事。明确不做的事：不做 Boss 独立场景（切场景打断割草流）；不另起内存管理（复用 Enemy 池，maxSize 拉高）。
- **佐证**：扩展点对应 `Data/DifficultyCurve.cs`（加 bossSchedule）、`Gameplay/EnemyDirector.cs`（bossActive 插播）、`Core/GameEvents.cs`（加事件）、`Gameplay/Juice.cs` + `CameraRig.cs`（表现复用）
- **兜底**：Boss AI 的状态机/行为树选型我答得清概念，但 demo 没实现——我诚实说这是设计推演，不是已验证代码；真做会先做一个"两阶段、两种弹幕"的最小 Boss 跑通全链路再加阶段。

### Q26｜如果要支持第二把武器，现有代码要怎么演进？

- **答**：现状是单武器：PlayerController 一个 _startingWeapon 字段，WeaponController 持单个 _data，Equip 直接覆盖。演进五步，按依赖顺序：① 抽象：WeaponData 加行为类型（enum：自动索敌/环绕/近战），控制器拆成 WeaponBehaviour 基类加每类一个 Tick 实现——第二把武器一加，行为就不同构了，这正是我在敌人系统里说的"该上继承/策略的时机"，我不为现在的单武器提前抽象；② 槽位：Player 持 List&lt;WeaponBehaviour&gt;，升级卡池加"新武器"类型的 UpgradeData；③ 索敌共享：抽 TargetingService，0.1s 节流一次多武器共用目标缓存——现在各自节流的写法复制两份就浪费了；④ 池：不同弹丸预制体各自 Register，现有 PoolService 天然支持；⑤ HUD 加武器槽图标。风险我点名两个：每武器独立 Update 放大节流成本，改中央 tick 驱动；三选一卡池被稀释，需要按槽位过滤的权重系统。
- **佐证**：`Gameplay/WeaponController.cs` · `Equip`（注释"出生武器或未来多武器系统"，当初就留了口）、`FindNearestEnemy`（要抽共享的部分）；`Gameplay/PlayerController.cs` · `_startingWeapon`
- **兜底**：环绕类武器（不索敌、绕玩家转）和现有弹丸模型不同构，这块是推演；真做时先做行为最接近的第二把（改弹速/弹道），验证槽位框架再上异构武器——先同构后异构，降低一次改对的要求。

### Q27｜这个项目如果要上微信小游戏，要改哪些东西？

- **答**：先亮底牌：我做过功课、能讲清风险点，没实际打包过。按风险排序：① 编译与首包：WebGL/小游戏走 IL2CPP 编成 WASM，微信有首包体积硬限制（4MB 量级）、代码包 20MB 上限——应对是 Brotli 压缩加资源走 CDN 分包远端加载，包里只留逻辑；② 字体是天然友好项：TMP 图集只烘了用到的字符，不用带整个字库；③ 渲染降档：Bloom 在移动 GPU 是主要开销，备一份低配 URP Asset（关 Bloom、降分辨率比例）；④ 音频管线：Editor 合成的 WAV 是无损 PCM，要转 Vorbis/ADPCM——是资源管线改动不是代码改动；⑤ 输入：小游戏没键盘，Input System 的 On-Screen Controls 挂虚拟摇杆——移动读取已经收口在 ReadMoveInput 一个方法里，改造成本集中；⑥ 存档：SaveSystem 只用了 File.ReadAllText/WriteAllText 最小 API 面，persistentDataPath 映射平台文件系统，适配点收敛；⑦ 池预热在弱设备加载期可能卡，拆到 loading 分帧做。游戏代码层基本不用动——URP 2D、新版 Input System、UGUI、UnityEngine.Pool 都在团结引擎（Unity 2022.3 LTS 血统）支持范围内。
- **佐证**：`Core/SaveSystem.cs`（最小 API 面）；`EditorTools/DemoSceneBuilder.cs` · `EnsureCjkFont`（静态烘焙字符集）；`Gameplay/PlayerController.cs` · `ReadMoveInput`（输入收口）
- **兜底**：明确说"以上是迁移 checklist 推演，实测过的只有 PC 端"；被追团结引擎具体 API 差异，我答了解方向、以上手后的官方文档为准——不装。

### Q28｜你的性能预算是怎么定的？同屏上限 90 是怎么权衡出来的？

- **答**：预算思路是"先定体验目标，再往每个系统发配额，超配额先砍内容"。目标 60fps、主循环 16.6ms。分离力是最大头：90 只怪、每只 0.15s 一次查询、每次最多 6 邻居，摊下来每帧约 30 次物理查询，可控。90 这个数有两重约束恰好重叠：物理侧是分离力和查询的配额上限；体验侧是密度超过这个值玩家已经分辨不过来——性能上限和体验上限重合，是最舒服的预算点。闸门设计是关键决策：Enemy.AliveCount 在 OnEnable/OnDisable 维护静态计数，超限本帧直接不刷——"宁可不刷，不能卡顿"，帧率优先级高于数量。配套还有宝石/飘字各自 maxSize（240/120）封顶内存。方法论一句话：预算不是"优化到跑不动为止"，是"配额制加总闸兜底"；上线前要用 Profiler 长测（soak）验证最坏情况而不是平均帧。
- **佐证**：`Data/DifficultyCurve.cs` · `maxAliveEnemies`（注释"超限就不刷，保证帧率稳定"）；`Gameplay/Enemy.cs` · `OnEnable/OnDisable`（AliveCount）；`Gameplay/EnemyDirector.cs` · `Update`
- **兜底**：诚实说 90 没有做过跨机型 Profiler 基准，是估算加实测手感的折中；被追问各系统毫秒数，我答"知道量级、没存基准数据，这是我工程流程该补的一环"。

### Q29｜这个项目哪里最值得重构？

- **答**：给三个候选，按痛感/收益比排序，观点明确。第一优先：GameOverPanel 的新纪录判定——它用 `Mathf.Approximately(best.survivalSeconds, RunTime) && best.kills == gm.Kills` 反推"是不是新纪录"，浮点近似加双条件，同秒同击杀的旧纪录会误判、浮点误差会漏判；而 SaveSystem.TryUpdateBestRecord 本来就返回 bool"是否写入"，正确做法是把返回值传下去，一个字段的事。第二优先：三套池并存——通用 PoolService 加 GemPool、PopPool 各写一遍 ObjectPool 构造和单例模板，统一成泛型注册表能删约 80 行且行为一致。第三优先：FindFirstObjectByType 散落在 XPGem.Collect、LevelUpPanel 等运行时路径——频率低但正确形态是引导器注入。另外主动交代一个已知缺口，严格说是 bug 不是重构项，优先级高于上面全部：Animator 的 Moving 参数运行时没人写，玩家切不了 Run。
- **佐证**：`UI/GameOverPanel.cs` 第 63 行判定；`Core/SaveSystem.cs` · `TryUpdateBestRecord`（已有返回值未利用）；三套池：`Core/PoolService.cs` / `Gameplay/XPGem.cs` · `GemPool` / `Gameplay/DamagePop.cs` · `PopPool`
- **兜底**：被问"为什么不现在就改"——demo 冻结在可演示状态，重构要带回归验证；这三条修法明确，属于"知道怎么改、等窗口期"，不是"不知道哪里有问题"。

### Q30｜AI 辅助开发，你怎么保证质量？

- **答**：先给观点：AI 辅助开发的质量问题本质是责任问题——产出可以是 AI 的，责任必须是人的。我的流程是七步循环：拆系统到类粒度 → 每次提示词固定带四样东西（技术栈、已定架构约束、职责清单、验收标准）→ 一次只生成一个类 → 人工按清单逐条审 → 批量编译 → Play 模式跑完整流程 → 新发现的坑沉淀回提示词模板。审查清单七项：API 版本正确性（只用 2022.3 真实存在的 API）、生命周期（单例去重、订阅对称、协程宿主）、GC（热路径不 new 不闭包）、事件泄漏、空引用（池对象禁用态访问）、timeScale 纪律、编辑器运行时分离。三个真实修正案例证明审查不是走过场：池反查表（AI 初稿要求调用方传预制体，违背"一行调用"验收标准，打回重写）、ClearAllSubscribers 兜底（AI 没推演跨场景时序，人补的）、暂停面板 OnEnable 订阅死锁（AI 理论上无懈可击，但 inactive 对象 OnEnable 根本不执行——只有运行验证抓得到）。还有一条纪律：AI 生成不合格是打回重写而不是人动手改——人改了 AI 下次还错，要修的是提示词。
- **佐证**：`docs/AI_WORKFLOW.md`（七步循环/审查清单/三案例全记录）；落地证据如 `scripts/Core/PoolService.cs` 每个类头部的中文设计意图注释——验收标准里就要求"注释说明设计决策"
- **兜底**：被问"AI 写的代码你怎么为它负责"——架构决策（状态机收口 timeScale、事件总线、组合优于继承）是人拍板的，AI 在约束内实现，行为用运行验证背书；这是"人管架构、AI 管实现"的分工，不是甩锅。

### Q31｜如果数值被策划改坏了，你怎么快速定位？

- **答**：我的数值体系有三个特征让定位很快。第一，数值资产是文本：SO 在 git 里就是 YAML，一条 diff 就看到"谁把 hpScale 第 5 分钟关键帧从 1.6 拖成了 6"；而且构建脚本幂等（NewAsset 已存在就载入旧的、保留改动），不存在"一键搭建覆盖了调好的值"这种坑。第二，数值入口收敛：影响平衡的配置只有四个 SO（Weapon/Enemy/Upgrade/Difficulty），嫌疑范围天然小。第三，防御性兜底：分离推力 ClampMagnitude 封顶、同屏上限封顶、ApplyUpgrade 是白名单 switch——改出离谱值会被钳住而不是崩。再查不出来就打两层事实对照：在 Enemy.Init 加一行初始化日志确认"资产里的值"和"运行时拿到的值"一致，不一致就往注入链路查。方法论一句话：先 diff 资产（静态事实），再打运行时快照（动态事实），两层对不上才怀疑代码。
- **佐证**：`EditorTools/DemoSceneBuilder.Prefabs.cs` · `NewAsset`（幂等注释"保留用户改动"）；`Gameplay/PlayerStats.cs` · `ApplyUpgrade`（switch 白名单）；`Gameplay/Enemy.cs` · `ComputeSeparation`（ClampMagnitude）
- **兜底**：Debug HUD（快捷键显示 Stats/难度快照）目前没实现，我承认这是"我会怎么做"而不是"已经做了"；git diff SO 的做法我真验证过——资产全文本可读，是资产代码化的红利。

### Q32｜你的项目离商业水准还差什么？

- **答**：按重要性排，每条给路线，不回避。① 内容深度：单武器、8 种升级、3 种怪，商业幸存者是十几武器×进化合成×几十升级——差的是内容量，好在 SO 驱动已验证"加内容零代码改动"，这是管线底子；② 音频工程：SFX 是合成 WAV、无混音总线、无音量设置——商业需要 AudioMixer 分组加设置持久化；③ 质量保障：零自动化测试——PlayerStats.AddXp 这种纯函数最该先补 EditMode 测试；存档无版本字段，BestRecord 加字段就没有向后兼容策略，要补版本号迁移；④ 数值验证：平衡靠手感没有遥测——商业会埋每局时长/死亡原因/升级选择分布反推曲线；⑤ 长测：没有 soak 测试，内存稳定性目前是推演加短测背书。最诚实的一条：从 demo 的"能玩"到商业的"能上线"，中间还隔着内容量、平台合规、性能长测、运营支持。我的定位：这个项目证明的是工程管线能力（一键搭建、数据驱动、解耦架构），不是产品完成度——而岗位要的正是前者打底。
- **佐证**：`Core/SaveSystem.cs`（BestRecord 无版本字段）；`EditorTools/SfxSynth.cs`（合成音频现状）；`Data/UpgradeData.cs` · `UpgradeKind`（8 种即当前内容上限）
- **兜底**：被问"哪个差距最先补"——测试加存档版本化（半天工作量、风险收益比最高），内容量是长期仗不着急；我的原则是先补会翻车的地方，再补能出彩的地方。

---

## 四、自检雷区（背答案前先过一遍）

1. timeScale 纪律：只有 GameManager 状态机加 HitStop 协议能写它——任何回答里都别出现"我顺手改了 timeScale"。
2. 订阅对称：说任何"我订阅了 X"，必须同时带上"在 OnDisable 退订"。
3. 池对象是二手机：说 Spawn 就要能报出 Init 的重置清单。
4. 敌人碰撞体是触发器不是实体碰撞——按真实代码讲（不参与物理互推、间距归分离力算法），接触伤害走 OnTriggerStay2D 加冷却。
5. Animator 的 Moving 参数运行时缺口：主动交代，别等面试官抓。
6. 没实测过的领域（团结引擎打包、SRP Batcher 对比、Deep Profile GC 数据）一律"做过功课、原理清楚、没实测"——诚实是这份卷子的及格线。
