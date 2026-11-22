using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using StarVortex.Core;
using StarVortex.Data;
using StarVortex.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StarVortex.Tests
{
    /// <summary>
    /// PlayMode 冒烟测试（Unity Test Framework）：一键搭建后的核心玩法链路自动化验证。
    /// 前置：先在编辑器里执行菜单 StarVortex/① 一键搭建 Demo 生成场景/资产；
    /// 场景或关键资产缺失时整组测试自动 Assert.Ignore 跳过，不计为失败。
    /// 项目无 asmdef：本文件独立测试程序集（StarVortex.Tests，无平台限制——PlayMode 测试不能放在 Editor-only 程序集）。
    /// 编辑器内可直接用 AssetDatabase，PlayMode 运行不受影响（打包前需移出构建）。
    /// </summary>
    public class SmokeTests
    {
        // ---------- 资产路径常量（与 DemoSceneBuilder.Settings 的常量保持一致） ----------
        private const string GameSceneName = "GameScene";
        private const string SceneGamePath = "Assets/Scenes/GameScene.unity";
        private const string SceneMainMenuPath = "Assets/Scenes/MainMenu.unity";
        private const string DataDirPath = "Assets/Data/Generated";
        private const string PrefabDirPath = "Assets/Prefabs/Generated";
        private const string DroneDataPath = DataDirPath + "/Enemy_Drone.asset";
        private const string DifficultyCurvePath = DataDirPath + "/DifficultyCurve_Main.asset";
        private const string EnemyPrefabPath = PrefabDirPath + "/Enemy.prefab";
        private const string TeardownSceneName = "~SmokeTests_Teardown";

        /// <summary>等武器自动开火的窗口（真实时间秒，索敌 0.1s + 首发延迟 0.2s 之外再留余量）。</summary>
        private const float FireWaitSeconds = 2.5f;

        /// <summary>波次导演压测时长（真实时间秒）：spawnRateStart=0.7/s 下必然出怪。</summary>
        private const float SpawnStressSeconds = 8f;

        // ============================ 生命周期 ============================

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            Time.timeScale = 1f;

            // 清掉可能残留的跨场景常驻管理器（首个测试运行时可能来自当前打开的场景）
            if (GameManager.Instance != null) Object.DestroyImmediate(GameManager.Instance.gameObject);
            else if (PoolService.Instance != null) Object.DestroyImmediate(PoolService.Instance.gameObject);

            // 场景缺失 → 整组冒烟测试跳过（先跑一键搭建）
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneGamePath) == null ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneMainMenuPath) == null)
            {
                Assert.Ignore($"场景资产缺失（{SceneGamePath} / {SceneMainMenuPath}），" +
                              "先运行菜单 StarVortex/① 一键搭建 Demo，再执行冒烟测试。");
            }

            // 加载 GameScene（Single 模式顺带卸掉上一测试遗留的场景）
            AsyncOperation load = SceneManager.LoadSceneAsync(GameSceneName);
            Assert.IsNotNull(load, "GameScene 的 LoadSceneAsync 不应返回空（场景是否损坏？）");
            while (!load.isDone) yield return null;

            // 等 Start() 装配完成（玩家 Stats、武器 Equip、波次导演的敌人池预热）
            for (int i = 0; i < 5; i++) yield return null;

            Assert.IsNotNull(GameManager.Instance, "GameScene 加载后应有 GameManager.Instance");
            Assert.IsNotNull(PoolService.Instance, "GameScene 加载后应有 PoolService.Instance");
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.timeScale = 1f;

            // 1) 清理动态玩法对象（敌人/弹丸/宝石/飘字；池内休眠实例随后随管理器一起销毁）
            DestroyAll<Enemy>();
            DestroyAll<Projectile>();
            DestroyAll<XPGem>();
            DestroyAll<DamagePop>();
            yield return null; // 让 Destroy 生效（OnDisable 归还 Enemy.AliveCount 等静态计数）

            // 2) 卸载 GameScene：Unity 不允许卸载最后一个场景，先垫一个空场景
            if (SceneManager.GetActiveScene().name == GameSceneName)
            {
                if (!SceneManager.GetSceneByName(TeardownSceneName).IsValid())
                    SceneManager.CreateScene(TeardownSceneName);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(GameSceneName);
                if (unload != null) while (!unload.isDone) yield return null;
            }

            // 3) 清理跨场景常驻管理器（Managers 上还挂着 Pool/Juice/Audio，一次性全清）
            if (GameManager.Instance != null) Object.DestroyImmediate(GameManager.Instance.gameObject);
            else if (PoolService.Instance != null) Object.DestroyImmediate(PoolService.Instance.gameObject);
        }

        private static void DestroyAll<T>() where T : Object
        {
            foreach (var obj in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                Object.Destroy(obj);
        }

        // ============================ 1. 资产存在性 ============================

        [UnityTest]
        public IEnumerator SceneAssetsExist()
        {
            // Arrange + Act：逐一检查场景 / 数据资产 / 预制体（缺失清单一次报全，便于排查搭建问题）
            var missing = new List<string>();
            void Check(string path)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(path) == null) missing.Add(path);
            }

            Check(SceneGamePath);
            Check(SceneMainMenuPath);
            Check($"{DataDirPath}/Weapon_PulseBlaster.asset");
            Check(DroneDataPath);
            Check($"{DataDirPath}/Enemy_Darter.asset");
            Check($"{DataDirPath}/Enemy_Brute.asset");
            Check(DifficultyCurvePath);
            Check($"{DataDirPath}/Upgrade_Damage.asset");
            Check($"{DataDirPath}/Upgrade_FireRate.asset");
            Check($"{DataDirPath}/Upgrade_Speed.asset");
            Check($"{DataDirPath}/Upgrade_MaxHp.asset");
            Check($"{DataDirPath}/Upgrade_Pickup.asset");
            Check($"{DataDirPath}/Upgrade_Multishot.asset");
            Check($"{DataDirPath}/Upgrade_Crit.asset");
            Check($"{DataDirPath}/Upgrade_Regen.asset");
            Check($"{PrefabDirPath}/Projectile.prefab");
            Check(EnemyPrefabPath);
            Check($"{PrefabDirPath}/XPGem.prefab");
            Check($"{PrefabDirPath}/DamagePop.prefab");

            if (missing.Count > 0)
                Assert.Ignore("关键场景/资产缺失，先运行菜单 StarVortex/① 一键搭建 Demo。缺失清单："
                              + string.Join("、", missing));

            // Assert：层注册（运行时代码 LayerMask.NameToLayer("Enemy") 等依赖这些层号）
            Assert.That(LayerMask.NameToLayer("Player"), Is.EqualTo(6), "层 Player 应注册在索引 6");
            Assert.That(LayerMask.NameToLayer("Enemy"), Is.EqualTo(7), "层 Enemy 应注册在索引 7");
            Assert.That(LayerMask.NameToLayer("PlayerBullet"), Is.EqualTo(8), "层 PlayerBullet 应注册在索引 8");
            Assert.That(LayerMask.NameToLayer("XPGem"), Is.EqualTo(9), "层 XPGem 应注册在索引 9");

            yield break;
        }

        // ============================ 2. 场景引导 ============================

        [UnityTest]
        public IEnumerator GameScene_boots_core_systems()
        {
            // Arrange：UnitySetUp 已加载 GameScene。Act：再跑 30 帧让启动/首帧逻辑充分执行。
            for (int i = 0; i < 30; i++) yield return null;

            // Assert：核心系统全部就位
            Assert.IsNotNull(GameManager.Instance, "跑 30 帧后 GameManager.Instance 应存在（Managers 装配失败？）");
            Assert.IsNotNull(PoolService.Instance, "跑 30 帧后 PoolService.Instance 应存在（对象池服务缺失）");

            var cam = Camera.main;
            Assert.IsNotNull(cam, "GameScene 应有标记 MainCamera 的相机");
            var rig = Object.FindFirstObjectByType<CameraRig>();
            Assert.IsNotNull(rig, "GameScene 应有 CameraRig（相机跟随/震屏）");
            Assert.AreSame(cam, rig.GetComponent<Camera>(), "CameraRig 应挂在主相机同一物体上");
            Assert.AreSame(rig, CameraRig.Instance, "CameraRig.Instance 应指向场景中的实例");

            var player = Object.FindFirstObjectByType<PlayerController>();
            Assert.IsNotNull(player, "GameScene 应有 PlayerController");
            Assert.IsNotNull(player.Stats, "PlayerController.Start 后运行时属性面板 Stats 应已装配");

            var director = Object.FindFirstObjectByType<EnemyDirector>();
            Assert.IsNotNull(director, "GameScene 应有 EnemyDirector（波次刷怪导演）");

            yield break;
        }

        // ============================ 3. 经验曲线 ============================

        [UnityTest]
        public IEnumerator PlayerStats_level_curve()
        {
            // Arrange：纯运行时数据类，不依赖场景
            var stats = new PlayerStats();
            Assert.That(stats.Level, Is.EqualTo(1), "新建 PlayerStats 应从 1 级开始");
            Assert.That(stats.Xp, Is.EqualTo(0), "新建 PlayerStats 经验应为 0");
            Assert.That(stats.XpToNext, Is.EqualTo(PlayerStats.XpForLevel(1)),
                "XpToNext 应等于 XpForLevel(1) 的曲线值");

            // Act：恰好喂满一级所需经验
            int ups = stats.AddXp(stats.XpToNext);

            // Assert：恰好升 1 级、无溢出
            Assert.That(ups, Is.EqualTo(1), "AddXp(XpToNext) 返回值应为 1（恰好升 1 级）");
            Assert.That(stats.Level, Is.EqualTo(2), "喂满 XpToNext 后应为 2 级");
            Assert.That(stats.Xp, Is.EqualTo(0), "恰好喂满一级时不应有溢出经验");

            // Act：一次灌入 2→5 级全部所需经验 + 5 点溢出
            int need = 0;
            for (int lv = 2; lv <= 4; lv++) need += PlayerStats.XpForLevel(lv);
            const int carry = 5;
            int multiUps = stats.AddXp(need + carry);

            // Assert：连升 3 级且溢出结转
            Assert.That(multiUps, Is.EqualTo(3), "一次灌入多级经验应连续升级 3 次");
            Assert.That(stats.Level, Is.EqualTo(5), "连升后应为 5 级");
            Assert.That(stats.Xp, Is.EqualTo(carry), "溢出经验应结转到下一级（Xp 应为 5）");
            Assert.That(stats.XpToNext, Is.EqualTo(PlayerStats.XpForLevel(5)),
                "升级后 XpToNext 应按新等级的曲线重新计算");

            yield break;
        }

        // ============================ 4. 对象池往返 ============================

        [UnityTest]
        public IEnumerator PoolService_round_trip()
        {
            // Arrange：UnitySetUp 已加载 GameScene，PoolService 就绪；
            // 解析波次导演注册进池的同一个 Enemy 预制体键
            GameObject prefab = ResolveEnemyPoolPrefab();
            Assert.IsNotNull(prefab, "应能从 EnemyDirector 序列化字段或资产路径解析出 Enemy 预制体");

            Vector3 farPos = new Vector3(60f, 60f, 0f); // 远离玩家：未 Init 的敌人不会追击/触碰玩家
            PoolService.Instance.Register(prefab, prewarm: 4, maxSize: 64); // 导演已注册时此调用幂等跳过

            // Act：Spawn → Despawn → Spawn
            var first = PoolService.Instance.Spawn(prefab, farPos, Quaternion.identity);
            Assert.IsNotNull(first, "Spawn 应返回实例");
            Assert.IsTrue(first.activeSelf, "Spawn 出的实例应为激活状态");
            yield return null;

            PoolService.Instance.Despawn(first);
            Assert.IsFalse(first.activeSelf, "Despawn 后实例应归还池中（去激活而非销毁）");
            yield return null;

            var second = PoolService.Instance.Spawn(prefab, new Vector3(-40f, 30f, 0f), Quaternion.identity);

            // Assert：同一实例被复用，且位置/激活状态被重置
            Assert.AreSame(first, second, "第二次 Spawn 应复用同一实例（对象池回收复用，而不是重新 Instantiate）");
            Assert.IsTrue(second.activeSelf, "复用取出的实例应重新激活");
            Assert.That(second.transform.position, Is.EqualTo(new Vector3(-40f, 30f, 0f)),
                "复用实例应被设置新的生成位置");

            PoolService.Instance.Despawn(second); // 清理，不留活体污染后续测试
        }

        // ============================ 5. 击杀掉落 ============================

        [UnityTest]
        public IEnumerator Enemy_dies_drops_xp()
        {
            // Arrange：真实对局环境（Playing 状态 + 场景内玩家 + 对象池/宝石池）
            yield return StartRun();
            var player = Object.FindFirstObjectByType<PlayerController>();
            Assert.IsNotNull(player, "GameScene 应有 PlayerController（掉宝石的 XP 加成接收方）");
            var droneData = AssetDatabase.LoadAssetAtPath<EnemyData>(DroneDataPath);
            Assert.IsNotNull(droneData, "Enemy_Drone 数据资产应存在");
            GameObject prefab = ResolveEnemyPoolPrefab();
            Assert.IsNotNull(prefab, "应能解析出 Enemy 预制体");

            int killsBefore = GameManager.Instance.Kills;
            int aliveBefore = Enemy.AliveCount;

            // Act：玩家附近生成敌人 → Init（非精英、锁定真实玩家）→ 一击致死
            Vector3 spawnPos = player.transform.position + new Vector3(4f, 0f, 0f);
            var go = PoolService.Instance.Spawn(prefab, spawnPos, Quaternion.identity);
            Assert.IsNotNull(go, "敌人应能通过 PoolService 生成");
            var enemy = go.GetComponent<Enemy>();
            Assert.IsNotNull(enemy, "生成的实例应带 Enemy 组件");
            enemy.Init(droneData, false, player.transform);
            yield return null;
            Assert.That(Enemy.AliveCount, Is.GreaterThanOrEqualTo(aliveBefore + 1),
                "敌人生成后 Enemy.AliveCount 应至少 +1");

            enemy.TakeDamage(new DamageInfo(droneData.maxHp * 2f, go.transform.position));

            // Assert：死亡当帧 Kills 已增加；场景出现 XPGem；实例归还对象池
            Assert.That(GameManager.Instance.Kills, Is.EqualTo(killsBefore + 1),
                "敌人死亡当帧 GameManager.Kills 应恰好 +1");
            var gem = Object.FindFirstObjectByType<XPGem>();
            Assert.IsNotNull(gem, "敌人死亡应掉落 XPGem（经验宝石）");
            Assert.IsFalse(go.activeSelf, "敌人死亡后应归还对象池（去激活）");
            Assert.That(Enemy.AliveCount, Is.EqualTo(aliveBefore),
                "敌人死亡归还池后 Enemy.AliveCount 应回落");

            yield return null;
            Assert.IsNotNull(Object.FindFirstObjectByType<XPGem>(), "下一帧经验宝石应仍在场景中等待磁吸拾取");
        }

        // ============================ 6. 自动索敌开火 ============================

        [UnityTest]
        public IEnumerator Weapon_fires_at_nearby_enemy()
        {
            // Arrange：真实对局环境；在玩家索敌半径（targetRadius=10）内放一个敌人
            yield return StartRun();
            var player = Object.FindFirstObjectByType<PlayerController>();
            Assert.IsNotNull(player, "GameScene 应有 PlayerController");
            var droneData = AssetDatabase.LoadAssetAtPath<EnemyData>(DroneDataPath);
            Assert.IsNotNull(droneData, "Enemy_Drone 数据资产应存在");
            GameObject prefab = ResolveEnemyPoolPrefab();
            Assert.IsNotNull(prefab, "应能解析出 Enemy 预制体");

            Vector3 spawnPos = player.transform.position + new Vector3(3f, 0f, 0f);
            var go = PoolService.Instance.Spawn(prefab, spawnPos, Quaternion.identity);
            Assert.IsNotNull(go, "敌人应能通过 PoolService 生成");
            go.GetComponent<Enemy>().Init(droneData, false, player.transform);

            // Act：真实时间窗口内轮询弹丸（对象池预热出的休眠实例是Inactive，不会被误判）
            float deadline = Time.realtimeSinceStartup + FireWaitSeconds;
            Projectile projectile = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                projectile = Object.FindFirstObjectByType<Projectile>();
                if (projectile != null) break;
                yield return null;
            }

            // Assert：武器自动索敌并发射；弹丸在 PlayerBullet 层（命中矩阵依赖）
            Assert.IsNotNull(projectile,
                $"等待 {FireWaitSeconds:0.#} 秒内武器应向索敌范围内的敌人发射弹丸（场景未出现 Projectile）");
            Assert.That(projectile.gameObject.layer, Is.EqualTo(8), "弹丸应在 PlayerBullet 层（索引 8）");

            // 清理测试生成的敌人：若它已被弹丸打死并归还池中，重复 Despawn 会触发池的 collectionCheck
            if (go != null && go.activeSelf) PoolService.Instance.Despawn(go);
        }

        // ============================ 7. 刷怪上限 ============================

        [UnityTest]
        public IEnumerator Spawning_respects_alive_cap()
        {
            // Arrange：真实对局 + 难度曲线资产
            yield return StartRun();
            var difficulty = AssetDatabase.LoadAssetAtPath<DifficultyCurve>(DifficultyCurvePath);
            Assert.IsNotNull(difficulty, "DifficultyCurve_Main 资产应存在");

            // Act：跑满 8 秒真实时间，逐帧记录同屏存活峰值；
            // 期间兜底处理升级暂停（测试环境无输入，自动关卡）与死亡结算（提前结束）
            int peakAlive = 0;
            float deadline = Time.realtimeSinceStartup + SpawnStressSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var gm = GameManager.Instance;
                if (gm == null) Assert.Fail("对局运行期间 GameManager.Instance 不应丢失");
                if (gm.State == GameState.LevelUp) gm.FinishLevelUp();
                if (gm.State == GameState.GameOver) break;
                peakAlive = Mathf.Max(peakAlive, Enemy.AliveCount);
            }

            // Assert：累计生成 >0，且同屏存活始终不超上限
            Assert.That(peakAlive, Is.GreaterThan(0),
                $"{SpawnStressSeconds:0} 秒内波次导演应至少生成过 1 个敌人");
            Assert.That(peakAlive, Is.LessThanOrEqualTo(difficulty.maxAliveEnemies),
                "同屏存活峰值不得超过 DifficultyCurve_Main.maxAliveEnemies（帧率保护阀失效？）");
            Assert.That(Enemy.AliveCount, Is.LessThanOrEqualTo(difficulty.maxAliveEnemies),
                "测试结束时同屏存活数不得超过 maxAliveEnemies");
            Assert.That(GameManager.Instance.RunTime, Is.GreaterThan(4f),
                "对局应处于运行状态并正常计时（若被升级/暂停冻结说明状态机未恢复）");
        }

        // ============================ 辅助 ============================

        /// <summary>
        /// 启动一局：走 GameManager.StartNewRun（状态→Playing、重置击杀/计时、异步重载 GameScene）。
        /// 等待依据是"激活场景句柄变化"，避免同名旧场景导致提前通过。
        /// </summary>
        private static IEnumerator StartRun()
        {
            Assert.IsNotNull(GameManager.Instance,
                "启动对局前 GameManager.Instance 应已存在（UnitySetUp 已加载 GameScene）");
            Scene sceneBefore = SceneManager.GetActiveScene();
            GameManager.Instance.StartNewRun();

            float deadline = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().handle == sceneBefore.handle)
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail("StartNewRun 后 10 秒内 GameScene 未完成重载（场景是否在 Build Settings？）");
                yield return null;
            }

            // 再等几帧让新场景的 Start() 装配完成（玩家 Stats / 武器 Equip / 导演预热）
            for (int i = 0; i < 3; i++) yield return null;

            Assert.That(GameManager.Instance.State, Is.EqualTo(GameState.Playing),
                "StartNewRun 后应处于 Playing 状态");
            Assert.IsNotNull(Object.FindFirstObjectByType<PlayerController>(),
                "对局开始后场景应有 PlayerController");
        }

        /// <summary>
        /// 解析波次导演注册进对象池的同一个 Enemy 预制体键：
        /// 优先反射读 EnemyDirector._enemyPrefab（其池键即该组件的 gameObject），
        /// 兜底从资产路径加载——保证测试 Spawn 用的就是导演已注册的那个池。
        /// </summary>
        private static GameObject ResolveEnemyPoolPrefab()
        {
            var director = Object.FindFirstObjectByType<EnemyDirector>();
            if (director != null)
            {
                var field = typeof(EnemyDirector).GetField("_enemyPrefab",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field?.GetValue(director) is Enemy comp && comp != null)
                    return comp.gameObject;
            }

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            return loaded != null ? loaded.GetComponent<Enemy>().gameObject : null;
        }
    }
}
