# AI 辅助开发工作流——《星涡幸存者》是怎么写出来的

> 本项目由个人开发者使用 AI（ZCode/GLM）辅助编码，人工负责架构决策、代码审查与验证。这个流程与岗位 JD 强调的"AI 辅助开发 / 提示词工程 / 代码审查"一一对应。

## 为什么这个项目和 JD 的 AI 要求一致

- JD 要求的不是"会用 AI 补全"，而是**把 AI 当成受控的生产力工具**：给足上下文、约束输出、逐条审查、批量验证。本项目全程按这个纪律执行。
- 5000 行 C# 由 AI 生成初稿，但**架构决策（状态机收口 timeScale、事件总线、组合优于继承）由人拍板**，AI 负责在约束内实现——这是"人管架构、AI 管实现"的分工，而不是"AI 写啥用啥"。
- 每个系统都有真实的人工审查修正记录（见文末三例），证明流程里"审查"不是走过场。

## 工作流拆解（七步循环）

1. **需求 → 拆系统**：先把玩法拆成 Core / Gameplay / UI / Data / EditorTools 五层，再拆到类粒度（一个提示词只做一个系统），明确每个类的输入输出和边界。
2. **给 AI 上下文**：每次提示词固定带四样东西——项目技术栈（Unity 2022.3 LTS / C# 9 / 新版 Input System）、已定架构约束（如"timeScale 只许 GameManager 改"）、本系统的职责清单、验收标准（如"Spawn/Despawn 一行调用"）。
3. **生成**：要求 AI 输出完整可编译的单文件，带中文注释说明设计意图；一次只生成一个类，禁止顺手改别的文件。
4. **人工审查**：按下方审查清单逐条过，重点是 API 版本正确性和生命周期，不合格直接打回重写而不是人动手改（改了 AI 下次还错）。
5. **批量编译验证**：每完成一批系统就整体编译一次，报错集中修；编译通过才进 Git。
6. **运行验证**：Play 模式跑完整流程（进战斗→击杀→升级→暂停→死亡→结算），配合 Profiler 看对象池和节流是否真的生效。
7. **迭代**：把审查发现的新约束**沉淀回提示词模板**（比如"所有事件订阅必须 OnEnable/OnDisable 对称"），下一次生成不再犯错。

## 三个真实提示词示例

### 示例 1：统一对象池服务设计

```text
【角色】你是资深 Unity 程序，为我审查并实现一个 2D 割草游戏的对象池服务。
【技术栈】Unity 2022.3 LTS，C# 9。禁止使用 2021 之前才有的过时 API。
【背景】游戏每局生成上千个对象（子弹/敌人/宝石/飘字），Instantiate/Destroy 会造成 GC 尖峰。
已定架构：全局单例模式（Instance 属性 + Awake 去重 + DontDestroyOnLoad），
服务类放在 StarVortex.Core 命名空间。
【职责】
1. 基于 UnityEngine.Pool.ObjectPool 实现（不许手写池逻辑），
   提供 Register(prefab, prewarm, maxSize) / Spawn(prefab, pos, rot) / Despawn(instance) 三个公开方法；
2. Spawn 时若未注册则自动注册（懒初始化）；
3. Register 时预热水位：先 Get 再 Release 一轮，把 Instantiate 成本挪到加载期；
4. collectionCheck 只在 Editor 下开启（Application.isEditor）；
5. Despawn 只拿到实例，必须能反查归属的池——请设计实例→预制体的反查表；
6. 所有池对象挂到统一父节点，防止 Hierarchy 刷爆。
【验收标准】调用方一行代码完成取还；无 GC 分配的热路径（Spawn/Despawn 不 new 对象，
反查表复用键）；注释说明每条设计决策的理由。
```

### 示例 2：HitStop 与暂停态的冲突处理

```text
【角色】Unity 技术面试官风格，先指出我的方案漏洞，再给正确实现。
【背景】我的游戏里 timeScale 有多个来源：GameManager 状态机在 LevelUp/Paused/GameOver
三态下把它改成 0；打击感系统 Juice.HitStop 要把 timeScale 瞬间压到 0.05 再恢复。
已知问题：停帧进行中玩家按下暂停，状态机把 timeScale 改成 0，随后 HitStop 协程结束
把备份的 1 还原回去——暂停被破坏。
【约束】
1. timeScale 的常规修改只允许出现在 GameManager 状态机；
2. HitStop 是唯一例外，必须设计互斥协议；
3. 恢复计时必须用真实时间（WaitForSecondsRealtime）；
4. 停帧期间重复触发不得延长停帧时间（取 max 不叠加）；
5. 玩家受伤 0.05s，精英死亡 0.06s，数值作为常量可调。
【要求】给出 Juice.HitStop 的完整实现，说明三层防护各自拦截哪种时序竞态：
进入前检查状态、恢复前再检查状态、备份与恢复的原子性。
【验收】任意顺序触发 HitStop/暂停/升级/死亡，timeScale 终值始终等于状态机的预期值。
```

### 示例 3：程序化美术生成器

```text
【角色】图形方向 Unity 技术专家。
【任务】写一个 Editor 工具（MenuItem 触发），纯代码生成霓虹矢量风格的全部游戏贴图，
不下载任何外部素材、不引入任何图片库，零版权风险。
【风格规范】深蓝黑底 #0B0E1A；霓虹描边 + 发光核心的配色表（玩家青、敌人红/橙/紫、
宝石品红、弹丸暖白）；固定随机种子保证可重复执行结果一致；覆盖式写入 Assets/Art/Generated。
【实现要求】
1. 用内置软件光栅器逐像素绘制：符号距离场（SDF）计算形状内外，
   smoothstep 在边缘羽化实现抗锯齿——不要用 Graphics.Draw 系 API；
2. 输出约 30 张 PNG：玩家序列帧 4 张、三种敌人各 2 帧、宝石、弹丸、UI 图标、背景星点；
3. PNG 保持 LDR，HDR 提亮在运行时用 ColorUsage(HDR) 材质色实现（注释里写明这个取舍）；
4. 统一 PixelsPerUnit = 100，与物理尺寸（EnemyData.radius）对齐。
【验收】菜单一键运行后无需手工处理即可被预制体引用；重复运行幂等。
```

## AI 代码审查清单（每份生成代码逐条过）

| 检查项 | 重点 |
| --- | --- |
| API 版本正确性 | 只用 2022.3 存在的 API（如 FindFirstObjectByType 而非过时的 FindObjectOfType）；新 Input System 写法（Keyboard.current 轮询） |
| 生命周期 | 单例 Awake 去重 + OnDestroy 清 Instance；OnEnable/OnDisable 订阅对称；协程宿主销毁时自动停止 |
| GC 分配 | Update/FixedUpdate 热路径不 new、不闭包、不装箱；高频传参用 struct + in；物理查询结果不每帧分配新集合 |
| 事件订阅泄漏 | 静态事件订阅必须与对象生命周期绑定；场景切换是否有 ClearAllSubscribers 兜底 |
| 空引用 | 池对象归还后再访问会拿到 disabled 对象——取用前判空；Find 的结果判空；跨场景引用（Camera 等不随 DontDestroyOnLoad 存活） |
| timeScale 纪律 | 除状态机与 HitStop 协议外，任何代码不得写 Time.timeScale |
| 编辑器/运行时分离 | Editor 代码不进运行时程序集；发布版关闭 collectionCheck 等仅调试逻辑 |

## 人工审查修正的真实案例（AI 初稿 → 修正后）

**案例 1：对象池实例 → 预制体反查表。** AI 初稿的 `Despawn(GameObject)` 要求调用方同时传实例和预制体（像官方 ObjectPool.Release 一样），每个系统都得自己记住"我是从哪个池出来的"。审查指出这违背"一行调用"的验收标准，且调用方传错预制体会归还进错误的池——于是在 PoolService 加 `_instanceToPrefab` 反查表，Spawn 时登记、Despawn 时反查；因为键是复用中的实例、数量稳定，字典不膨胀。修正后 `Projectile.ReturnToPool` 只剩一行 `PoolService.Instance.Despawn(gameObject)`。

**案例 2：事件总线 ClearAllSubscribers 防跨场景泄漏。** AI 初稿只写了事件定义和 Publish，退订全靠各 UI 类自觉。审查推演了"面板在 OnDisable 前场景已卸载"的时序：静态事件的委托持有对象引用，订阅者不退订就永远不被 GC——泄漏且会触发对已销毁对象的调用。修正：GameEvents 增加 `ClearAllSubscribers()` 全量清空，挂在 GameManager（场景常驻者）的 OnDestroy 里做唯一兜底；同时约定 UI 面板仍保持 OnEnable/OnDisable 对称订阅作为常规路径。

**案例 3：暂停面板 OnEnable 订阅不生效，改由引导器驱动。** AI 初稿让 PausePanel 在自己的 OnEnable 里订阅 GameStateChanged 来控制显隐——但面板初始就是 inactive 的，OnEnable 根本不会执行，订阅从未建立，暂停面板永远弹不出来。这个 bug 是运行验证阶段抓到的，AI 自己无法发现。修正思路：显隐的驱动方不能是被驱动的对象自己——把 PausePanel 降级为纯视图（只挂按钮回调），订阅 GameStateChanged 的职责移交给 `GameSceneBootstrap` 引导器，它 Start 时按当前状态先设一次初始显隐，再监听后续切换。这个案例后来沉淀成了提示词模板里的固定约束："UI 面板显隐由引导器/状态机驱动，面板自身只做展示"。

## 一句话总结

> AI 负责把"说清楚的需求"变成"能编译的代码"，人负责说清楚需求、守住架构约束、按清单审查、批量验证——产出是 AI 的，责任和质量是人的。
