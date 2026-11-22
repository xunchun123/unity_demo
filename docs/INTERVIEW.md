# 《星涡幸存者 StarVortex》面试讲解稿

> 口语化可背；每个技术点按"对应系统 → 关键代码 → 设计决策 → 可能的追问与回答"组织。文件名均指 `scripts/` 下源码。

## 一、30 秒项目介绍

> 我独立做了一个 Unity 2D 幸存者割草游戏《星涡幸存者》，C# 大概五千行，全程 AI 辅助开发加人工审查。玩法是 WASD 移动、武器自动索敌、怪物按难度曲线成波刷出、击杀掉经验宝石磁吸拾取，升级弹三选一强化，十分钟一局，死亡结算存活时间、击杀数、等级，带最高分 Json 存档。技术上我比较能展开讲的有四块：基于官方 ObjectPool 的统一对象池、静态事件总线把玩法层和 UI 层彻底解耦、timeScale 全部收口到状态机避免多系统抢时间缩放，以及一个 Editor 工具链——美术、音效、中文字体、场景、预制体全部程序化生成，从空仓库到可玩只需要点一次菜单命令。

## 二、按 JD 技术点展开

### 1. C# 与面向对象

**对应系统**：伤害管线与运行时属性。
**关键代码**：`Core/Damage.cs`（`IDamageable` 接口 + `DamageInfo` struct）、`Gameplay/PlayerStats.cs`、`Core/GameEvents.cs`。

- `DamageInfo` 是 struct 且 `TakeDamage(in DamageInfo)` 用 in 参数：这是每颗子弹每次命中的高频路径，struct 避免堆分配 GC，in 避免传参拷贝。
- 弹丸 `Projectile.OnTriggerEnter2D` 只做 `GetComponentInParent<IDamageable>()`，不认识 Enemy 还是别的——依赖倒置，新增可受击物不改弹丸。
- `PlayerStats` 是普通 C# 类持有运行时可变属性，ScriptableObject 只是配置模板：绝不直接改 SO 资产，否则一改污染所有对局。
- **追问：接口和抽象类怎么选？** 答在这里选接口是因为"能被伤害"是能力不是身份，玩家和敌人没有公共父类需求；项目里没有用继承的地方，都是组合+数据。

### 2. 对象池：为什么基于 UnityEngine.Pool 而不是自己造轮子

**对应系统**：`Core/PoolService.cs`，子弹/敌人/宝石/飘字全走池；宝石另有泛型专用池 `GemPool`（`Gameplay/XPGem.cs`）。
**关键代码**：`Register` / `Spawn` / `Despawn` 三个方法。

- 为什么不手写：`UnityEngine.Pool.ObjectPool` 是 2021 起官方内置，把容易写错的边界都处理好了——`collectionCheck`（防双重归还）、`maxSize`（限制闲置对象数量，超出时归还即销毁）、`actionOnGet/OnRelease/OnDestroy` 回调约定。自己造轮子的价值在于理解原理，但生产代码里用官方实现更可靠，而且它是 `IObjectPool<T>` 泛型接口，我既能包 GameObject 也能直接包组件类型（`GemPool` 就是 `ObjectPool<XPGem>`）。
- 我在官方类之上加的两层价值：① 统一注册表 `Dictionary<GameObject, IObjectPool<GameObject>>`，所有系统共用一个入口；② **实例 → 预制体反查表** `_instanceToPrefab`——Despawn 只拿到实例不知道归属哪个池，反查表让归还变成一行调用；因为键是池内复用的实例、数量稳定，字典不会膨胀。
- 细节：预热水位（Register 时 Get 一轮再 Release 回去，把 Instantiate 成本挪到加载期）；`collectionCheck: Application.isEditor` 只在编辑器开，发布版省一次判定；实例名字去掉 "(Clone)" 保持 Hierarchy 干净；所有池对象挂统一父节点 `~Pools`。
- **追问：collectionCheck 开着有什么代价？** Release 时会遍历检查对象是否已在池内，发布版对象池使用纪律靠代码约定保证，所以敢关。
- **追问：池空了 maxSize 满了会怎样？** Get 时池空会走 createFunc 新建实例（自动扩容），maxSize 约束的是闲置对象数量，归还超量时直接销毁——不会丢引用也不会异常。

### 3. 事件总线：为什么静态 C# event 而不是 ScriptableObject 事件通道

**对应系统**：`Core/GameEvents.cs`，10 个事件覆盖状态切换、敌人死亡、经验、血量、击杀、计时。

- 决策：玩法层只负责 `PublishXxx`，UI 层只订阅——事件把依赖方向反转，Gameplay 对 UI 零依赖。发布方法用 `internal` 修饰，只有 Core 程序集内能发，UI 只能听不能发，权限清晰。
- 为什么不用 SO 事件通道：SO 通道的优势是 Inspector 可视化接线和天然跨场景；代价是每个事件一个资产文件、订阅生命周期要额外管理。本 demo 规模下静态 event 零分配、零资产、编译期可见，是"够用的最简方案"；规模变大升级到 SO 通道或消息系统的路径是明确的——这不是偷懒，是按规模选型。
- 泄漏防护是重点：静态事件的订阅者引用会拖住对象不让 GC 回收。两层防护——UI 面板全部 `OnEnable` 订阅 / `OnDisable` 退订对称；`GameManager.OnDestroy` 里 `GameEvents.ClearAllSubscribers()` 全量清空，覆盖"场景卸载但面板没来得及退订"的兜底场景。
- **追问：静态事件还有什么坑？** 域重载（Enter Play Mode Options）下静态状态不自动清空，要在 `InitializeOnLoad` 里处理；多场景下谁清空要有唯一负责人，我指定了 GameManager。

### 4. timeScale 多来源冲突怎么收口

**对应系统**：`Core/GameManager.cs` 状态机（MainMenu/Playing/LevelUp/Paused/GameOver）。

- 问题本质：升级弹窗要暂停、手动暂停要暂停、死亡结算要暂停、打击感 HitStop 也要压 timeScale——四个来源直接改 timeScale 必然互相覆盖。
- 方案：**普通状态切换的 timeScale 只在 GameManager 状态机里写**，每个状态对应确定的 timeScale 值；`EnterLevelUp` 入口有防御 `if (State != GameState.Playing) return`——处理"死亡瞬间同帧触发升级"这类竞态。
- 唯一例外是 `Juice.HitStop`（见第 6 条），它和状态机之间有一套互斥协议。
- **追问：为什么用状态机不用标志位叠加？** 标志位叠加（暂停计数）也能做，但状态机额外解决了"升级时能不能暂停""死亡时还能不能升级"这类互斥语义，`SetState` 里顺便广播 `GameStateChanged`，UI 面板显隐有了单一事实来源。

### 5. HitStop 与暂停态的互斥处理

**对应系统**：`Gameplay/Juice.cs` 的 `HitStop` / `HitStopRoutine`。

- 三条协议：① 前置检查 `State != Playing` 直接不生效——暂停/升级/结算态触发不了停帧；② 计时用 `WaitForSecondsRealtime`（真实时间），timeScale 压到 0.05 时不影响恢复计时；③ 恢复前**再查一次状态**，只有仍是 Playing 才还原备份的 timeScale——停帧进行中玩家开了暂停（状态机把 timeScale 改成 0），HitStop 结束不会把 0 覆盖回 1。
- 细节：`_pendingHitStop` 取 max 不叠加（停帧中再触发不延长）；玩家受伤停 0.05s，精英死亡停 0.06s。
- **追问：为什么压 0.05 而不是 0？** 完全冻结会像卡顿，留 5% 的微动是"顿"的感觉，这是打击感调出来的经验值。

### 6. 组合优于继承在敌人系统的体现

**对应系统**：`Gameplay/Enemy.cs` 一个类 + `Data/EnemyData.cs` 三份资产。

- drone/darter/brute 三种敌人共用一个 Enemy 类，差异全部是数据：血量、移速、接触伤害与冷却、分离半径、经验值、色调。行为框架（追击 + 分离 + 接触伤害 + 死亡结算）是一致的，所以不需要三个子类。
- 精英变体也不是子类：`Init(data, isElite, player)` 传 bool，血量 x4、体型 x1.4、经验 x6、色调提亮，全是数值倍率。
- 新增第四种敌人 = 新建一份 EnemyData 资产 + 在 DifficultyCurve 加一条权重曲线，零代码改动。
- **追问：什么时候才该用继承？** 行为结构不同的时候——比如 darter 要蓄力冲刺，它的 Update 拆成"蓄力/冲刺/硬直"三段，和通用追击框架不同构，那时才值得子类或策略模式。目前三种敌人的行为同构，继承只会带来空泛的基类钩子。

### 7. 分离力 / 索敌节流的性能考量

**对应系统**：`Gameplay/Enemy.cs` 的 `ComputeSeparation`、`Gameplay/WeaponController.cs` 的 `FindNearestEnemy`。

- 分离力朴素做法是全量两两算，100 只怪就是每帧约 1 万次对计算，O(n²)。我做了三层削减：① 每 0.15s 才重算一次，结果缓存在 `_separationPush` 里继续用；② `OverlapCircleAll` 结果最多采样 6 个邻居（for 循环截断），分离只需要局部防重叠，精确性可以牺牲；③ 每只怪的节流计时器初始相位随机错峰（`Random.value * 0.15f`），避免全体验算挤在同一帧。
- 索敌：`OverlapCircleAll` + Enemy 层过滤，0.1s 节流并缓存目标；比较距离用 `sqrMagnitude` 不开方；没有目标就停射，省性能也省弹丸。
- 还有一条兜底：`EnemyDirector` 里同屏上限 `maxAliveEnemies`（90），超限本帧不刷——帧率保护阀，宁可不刷不能卡顿。
- **追问：节流 0.1~0.15s 会不会看得出来？** 分离力是缓变量，肉眼无感；索敌目标 0.1s 的延迟在自动射击的玩法节奏里也察觉不到。节流对象都是"低频感知"的信息，每帧精确更新是浪费。

### 8. 震屏为什么用 trauma + Perlin

**对应系统**：`Gameplay/CameraRig.cs` 的 `LateUpdate`。

- 创伤模型：受击/击杀 `AddTrauma` 累积并 Clamp01，实际抖动量 `shake = trauma²`，每秒 1.6 线性衰减。平方的意义：低 trauma 时几乎不抖，叠加多次受击才明显——衰减前段剧烈后段柔和，量控制比效果本身重要，连续受伤不会把玩家晃吐。
- Perlin 噪声（`Mathf.PerlinNoise`，Time.time × 25 采样）驱动位移和旋转：噪声输出连续平滑，随机数抖动观感尖锐毛躁；位移 ±0.55、旋转 ±8°，三个通道用不同噪声坐标避免同步。
- 相机跟随用指数衰减 Lerp：`1 - exp(-smooth * dt)`，帧率无关的平滑系数，比裸 `Lerp(t)` 的帧率敏感写法稳。
- **追问：为什么不用 Cinemachine Impulse？** 可以用，但 trauma 模型 40 行就够，且参数（衰减、幅度、噪声）全部可控可讲；引入包收益不大。

### 9. UI 动画为什么要 unscaled time

**对应系统**：`UI/LevelUpPanel.cs`、`UI/GameOverPanel.cs`、`Core/Tween.cs`、`UI/HUDController.cs`。

- 三选一弹窗打开时 timeScale = 0，如果动画跑 `Time.deltaTime`，卡片弹入、数字滚动全部冻结——"暂停弹窗动画不动"是经典 bug。所以项目里的轻量补间工具 Tween（54 行手写协程）默认 `unscaled: true`，用 `Time.unscaledDeltaTime`。
- 涉及范围：升级卡片 ScaleIn（EaseOutBack 回弹）、结算数字滚动 CountTo、HUD 血条残影的 `MoveTowards` 也用 unscaledDeltaTime——死亡瞬间 timeScale 归零，但血条要继续流完。
- **追问：为什么不用 DOTween？** demo 用到的只有缩放弹出、淡入、数值滚动三件事，60 行手写零外部依赖，还能默认 unscaled；面试场景下讲清原理比引库更有说服力。真项目要用大量补间就上 DOTween，不重复造轮子。

### 10. 中文字体 SDF 方案

**对应系统**：`EditorTools/DemoSceneBuilder.cs` 的 `EnsureCjkFont`。

- 问题：TMP 默认字体（Liberation Sans）不含中文字形；中文游戏直接用动态字体运行时扩图集，首次显示新字符会卡顿，且打包后依赖目标机字体有不确定性。
- 方案：编辑器期从系统字体（微软雅黑）生成 TMP SDF 字体资产，并把游戏内用到的全部汉字**静态烘焙**进图集——打包后不依赖目标机字体，运行时零扩容卡顿。
- **追问：字符集怎么确定？** 收集全部 UI 字符串字面量烘焙；如果以后做玩家输入昵称，再对动态扩容做白名单兜底——静态为主、动态兜底是常见生产组合。

### 11. 组件系统与物理系统（Rigidbody2D / Collider2D）

**对应系统**：`Gameplay/PlayerController.cs`、`Gameplay/Enemy.cs`、`Gameplay/Projectile.cs`、`Gameplay/XPGem.cs`。

- 移动用 `Rigidbody2D.velocity` 在 FixedUpdate 驱动，不改 Transform：物理系统权威，与敌人碰撞检测一致；玩家 `CollisionDetectionMode2D.Continuous` 防高速穿透。
- 物理形态分工：弹丸用 Trigger（不阻挡移动，只做命中判定）；敌人碰撞体同样是 Trigger——同屏百怪不做物理互推，间距交给分离力算法，接触伤害由 `OnTriggerStay2D` 按冷却结算；玩家拾取圈是子物体上的 CircleCollider2D 触发器，半径随升级实时改。（面试主动讲这个取舍：敌人实体互撞会让物理求解器成为瓶颈）
- 一个省物理开销的细节：宝石不带 Rigidbody2D，触发检测由玩家侧（带刚体的一方）承担——同屏几百颗宝石省一大截。
- **追问：OnTriggerStay2D 每帧调用怎么处理？** 内部有接触冷却计时器 `contactCooldown`，冷却内直接 return；加上玩家 0.6s 受击无敌帧，双保险防同帧多次扣血秒杀。

### 12. UGUI 与 Animator

**对应系统**：HUD 血条残影缓动 / 底部经验条 / 三选一卡片 / 暂停 / 结算数字滚动；`EditorTools/DemoSceneBuilder.Anim.cs`。

- HUD 是事件驱动刷新：订阅 `GameEvents`，数值变化才更新；计时用 0.25s 节流的 `RunClockTick`，不做每帧刷新。血条残影：主条立刻跟随（2.5/s），白色残影慢速追赶（0.35/s），掉血有"先掉血、再流血"的痛感。
- Animator 也是完整工作流：玩家 Idle（4 帧 6fps）/ Run（4 帧 12fps），布尔参数 `Moving` 切换，过渡 0.1s；三种敌人各一个双帧循环。所有动画帧来自程序化生成的序列帧——没有外部素材也能走完整 Animator 流程，控制器由 Editor 脚本 `CreateAnimators` 生成。
- **追问：为什么状态切换用代码 `AddTransition` 而不是手摆？** 因为控制器本身是 Editor 脚本生成的，搭建可重复执行、结果可复现，这是"一键搭建"承诺的一部分。

### 13. Prefab / 场景管理与一键搭建

**对应系统**：`EditorTools/DemoSceneBuilder.cs`（partial 分 7 个文件）+ `Gameplay/GameSceneBootstrap.cs`。

- 一键搭建顺序：美术 → 音效 → 中文字体 → 数据资产与预制体 → 工程设置（层碰撞矩阵）→ 两个场景 → 全套 UGUI → 图集。首次打开项目 `InitializeOnLoadMethod` 弹确认框，用 marker 文件防重复执行。
- 运行期装配走引导器：`GameSceneBootstrap` 在 Start 里做"必须等场景跑起来才能做"的事（相机跟随目标注入、Canvas worldCamera 接线、暂停面板按当前状态显隐），场景内组件保持"哑组件"，依赖装配期注入，运行期零 Find 成本。
- **追问：为什么场景用脚本搭而不是手工摆？** 可复现是第一位的——换台机器、删掉 Library 重来，一次菜单命令回到完全相同的项目；也倒逼资产引用全部走代码接线，没有"拖丢了引用"这类隐形 bug。

### 14. 性能优化汇总与 Git

**汇总**（每条都在上面展开过）：对象池消灭 Instantiate/Destroy 尖峰；物理查询与分离力节流；同屏上限；struct + in 零 GC 伤害参数；MaterialPropertyBlock 闪白不实例化材质；粒子单系统 Emit 复用零实例化；UI 事件驱动 + 0.25s 节流；HDR 颜色喂 Bloom 用透明度换成本而不是叠层。

**Git**：分功能提交（对象池、事件总线、打击感各自独立提交），GitHub 仓库托管；因为资产全部代码生成，仓库里只有脚本和配置——没有二进制大文件，diff 可读、合并干净。

## 三、数值设计思路

**升级曲线**：`XpForLevel = 5 + level×4 + level²`（`PlayerStats.cs`）。线性项让前期 10~14 经验就升一级、节奏爽快；平方项在 6~8 级后接管，升级间隔明显拉开，形成"前期爽、后期抠"的张力。宝石经验值（drone 3 点、精英 x6）按这条曲线反推，保证前 2 分钟大约升 4~5 级。

**难度曲线**（`Data/DifficultyCurve.cs`，全部 AnimationCurve 可拖）：
- 刷怪速率 0.7/s → 3.2/s，9 分钟线性爬坡；
- 怪种入场：drone 全程权重 1；darter 第 2 分钟渐入（中期引入速度威胁，惩罚站桩）；brute 第 4 分钟渐入（后期肉盾，逼玩家堆伤害或弹幕数）；
- 怪物血量 1 → 1.6（5 分钟）→ 2.5 倍（10 分钟）；精英概率 0 → 2%（3 分钟）→ 8%（10 分钟）。
- 调法：先定节奏锚点——"每 30 秒玩家应感知到一次变化"（新怪种、精英首现、明显变密），再倒推每个曲线的关键帧；实测再微调。**平衡杠杆意识**：玩家 DPS 随升级接近指数堆叠（伤害 × 攻速 × 弹幕数相乘），敌人侧只有血量倍率和数量两个线性杠杆，所以后期压力主要靠密度和精英配比给。

**打击感数值**：停帧 0.05s、震屏 trauma 玩家受伤 0.35 / 击杀 0.07 / 精英 0.22——按"事件越重反馈越重"排比例，全在代码常量里，方便整体调档。

## 四、团结引擎 / 微信小游戏迁移谈资

> 没实际打包过就坦诚说"做过功课、能讲清风险点"，不装做过。

- **技术栈天然对齐**：团结引擎基于 Unity 2022.3 LTS 分支演进，本项目的 URP 2D Renderer、新版 Input System、UGUI、UnityEngine.Pool 都在它的支持范围内，游戏代码层基本不用动，迁移工作主要在平台适配层。
- **WebGL/小游戏适配要点**（按风险从大到小）：
  1. **首包与内存**：WebGL 下 Brotli 压缩 + 小游戏分包/远端资源加载；TMP 字体图集只烘用到的字符（本项目已这么做，天然友好）。
  2. **后处理降档**：Bloom 在移动 GPU 是主要开销，准备 URP Asset 降档方案（关 Bloom、降分辨率比例），低端机走无后处理配置。
  3. **音频格式**：Editor 合成的 WAV 是无损 PCM，发布到小游戏要转 Vorbis/ADPCM 压缩，这是资源管线改动而非代码改动。
  4. **存档路径**：`Application.persistentDataPath` 在小游戏环境映射到平台文件系统，`SaveSystem` 只用了 File.ReadAllText/WriteAllText 的最小 API 面，适配点集中。
  5. **输入**：小游戏没有键盘，要加虚拟摇杆（Input System 的 On-Screen Controls 可直接挂）。
  6. **对象池预热**：弱设备上加载期预热成本要实测，必要时把 prewarm 拆到 loading 过程分帧做。

## 五、反问面试官的问题清单

1. 团队目前的 AI 辅助开发流程是怎样的——AI 生成代码的比例、人工审查的标准和工具链是什么？
2. 小游戏项目里实际最常见的性能瓶颈是哪类：首包体积、运行时内存，还是 DrawCall/Overdraw？团队用什么工具定位？
3. 这个岗位入职前三个月的预期产出是什么？会先接玩法功能还是性能/工具方向？
4. 项目里程序和数值策划怎么分工，配置数据用什么管线（Excel 转表、SO 资产还是数据库）？
5. 团队怎么看待"单人独立开发 + AI 辅助"的工作模式，这类经验在团队协作里怎么转化？
