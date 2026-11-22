using UnityEngine;

namespace StarVortex.Data
{
    /// <summary>
    /// 难度曲线（波次导演的配置）：一局 10 分钟的怪物节奏全部在这里调。
    /// 面试点：把"第几分钟出什么怪、多密集"做成曲线资产，
    /// 平衡性调整不需要动 EnemyDirector 的代码，只拖曲线滑条。
    /// </summary>
    [CreateAssetMenu(fileName = "DifficultyCurve", menuName = "StarVortex/Difficulty Curve")]
    public class DifficultyCurve : ScriptableObject
    {
        [Header("刷怪密度")]
        [Tooltip("开局每秒刷怪数")]
        public float spawnRateStart = 0.7f;
        [Tooltip("第 spawnRateRampMinutes 分钟时达到的每秒刷怪数")]
        public float spawnRateEnd = 3.2f;
        public float spawnRateRampMinutes = 9f;

        [Header("同屏上限（性能保护）")]
        [Tooltip("同屏敌人上限——超限就不刷，保证帧率稳定")]
        public int maxAliveEnemies = 90;

        [Header("怪物组成（按时间的权重曲线）")]
        [Tooltip("小怪：恒定权重")]
        public AnimationCurve droneWeight = AnimationCurve.Constant(0f, 10f, 1f);
        [Tooltip("冲锋怪：第 2 分钟后逐渐加入")]
        public AnimationCurve darterWeight = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(2f, 0.2f), new Keyframe(4f, 0.6f), new Keyframe(10f, 1f));
        [Tooltip("坦克怪：第 4 分钟后逐渐加入")]
        public AnimationCurve bruteWeight = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(4f, 0.15f), new Keyframe(7f, 0.5f), new Keyframe(10f, 1f));

        [Header("数值成长")]
        [Tooltip("怪物血量随时间的成长（x = 分钟，y = 血量倍率）")]
        public AnimationCurve hpScale = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(5f, 1.6f), new Keyframe(10f, 2.5f));
        [Tooltip("精英怪概率（血量x4、体型x1.4、经验x6）随时间上升")]
        public AnimationCurve eliteChance = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(3f, 0.02f), new Keyframe(10f, 0.08f));
    }
}
