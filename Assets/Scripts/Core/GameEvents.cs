using System;

namespace StarVortex.Core
{
    /// <summary>
    /// 全局事件总线：用 C# event 做系统间解耦（玩法系统不直接引用 UI，UI 只订阅事件）。
    /// 面试点：为什么不用"下层持有上层引用"——事件反转依赖方向，Gameplay 层对 UI 层零依赖；
    /// 规模更大时可以升级为 ScriptableObject 事件通道，本 demo 规模下静态事件最直接、零分配。
    /// </summary>
    public static class GameEvents
    {
        /// <summary>游戏状态切换（UI 面板显隐的单一事实来源）。</summary>
        public static event Action<GameState> GameStateChanged;
        /// <summary>敌人死亡（参数：经验值、击杀来源位置）。</summary>
        public static event Action<int, UnityEngine.Vector3> EnemyDied;
        /// <summary>玩家经验变化（当前值 / 升级所需值）。</summary>
        public static event Action<int, int> XpChanged;
        /// <summary>玩家升级（新等级）。</summary>
        public static event Action<int> LevelUp;
        /// <summary>玩家 HP 变化（当前 / 上限）。</summary>
        public static event Action<float, float> PlayerHpChanged;
        /// <summary>玩家死亡。</summary>
        public static event Action PlayerDied;
        /// <summary>玩家受伤（伤害来源位置，用于屏幕方向反馈）。</summary>
        public static event Action<UnityEngine.Vector2> PlayerHurt;
        /// <summary>击杀数变化。</summary>
        public static event Action<int> KillCountChanged;
        /// <summary>游戏时间同步（每 0.25s 推一次，避免 UI 每帧刷新）。</summary>
        public static event Action<float> RunClockTick;
        // ---------- 《勇者远征》横版闯关版新增 ----------
        /// <summary>玩家蓝量变化（当前 / 上限）——火球术消耗与回复。</summary>
        public static event Action<float, float> PlayerManaChanged;
        /// <summary>天赋点变化（剩余可分配点数）。</summary>
        public static event Action<int> TalentPointsChanged;
        /// <summary>天赋属性变动（维度、最新值）——HUD/属性面板刷新。</summary>
        public static event Action<TalentKind> TalentSpent;
        /// <summary>获得爽点（爽点 ID）——战斗形态改变的广播。</summary>
        public static event Action<string> BoonAcquired;
        /// <summary>关卡阶段变化（关卡号、阶段描述：战斗中/目标达成/传送门开启）。</summary>
        public static event Action<int, string> LevelPhaseChanged;
        /// <summary>BOSS 血条变化（当前 / 上限）。</summary>
        public static event Action<float, float> BossHpChanged;

        internal static void PublishGameStateChanged(GameState state) => GameStateChanged?.Invoke(state);
        internal static void PublishEnemyDied(int xp, UnityEngine.Vector3 pos) => EnemyDied?.Invoke(xp, pos);
        internal static void PublishXpChanged(int cur, int need) => XpChanged?.Invoke(cur, need);
        internal static void PublishLevelUp(int level) => LevelUp?.Invoke(level);
        internal static void PublishPlayerHpChanged(float cur, float max) => PlayerHpChanged?.Invoke(cur, max);
        internal static void PublishPlayerDied() => PlayerDied?.Invoke();
        internal static void PublishPlayerHurt(UnityEngine.Vector2 from) => PlayerHurt?.Invoke(from);
        internal static void PublishKillCountChanged(int kills) => KillCountChanged?.Invoke(kills);
        internal static void PublishRunClockTick(float t) => RunClockTick?.Invoke(t);
        internal static void PublishPlayerManaChanged(float cur, float max) => PlayerManaChanged?.Invoke(cur, max);
        internal static void PublishTalentPointsChanged(int points) => TalentPointsChanged?.Invoke(points);
        internal static void PublishTalentSpent(TalentKind kind) => TalentSpent?.Invoke(kind);
        internal static void PublishBoonAcquired(string boonId) => BoonAcquired?.Invoke(boonId);
        internal static void PublishLevelPhaseChanged(int level, string phase) => LevelPhaseChanged?.Invoke(level, phase);
        internal static void PublishBossHpChanged(float cur, float max) => BossHpChanged?.Invoke(cur, max);

        /// <summary>场景卸载时清空所有订阅，防止跨场景引用残留对象（生命周期陷阱的经典来源）。</summary>
        public static void ClearAllSubscribers()
        {
            GameStateChanged = null;
            EnemyDied = null; XpChanged = null; LevelUp = null; PlayerHpChanged = null;
            PlayerDied = null; PlayerHurt = null; KillCountChanged = null; RunClockTick = null;
            PlayerManaChanged = null; TalentPointsChanged = null; TalentSpent = null;
            BoonAcquired = null; LevelPhaseChanged = null; BossHpChanged = null;
        }
    }
}
