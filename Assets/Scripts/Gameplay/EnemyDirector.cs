using System.Collections.Generic;
using StarVortex.Core;
using StarVortex.Data;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 波次导演：按难度曲线持续刷怪。
    /// 面试点：① 刷怪点选"玩家周围视野外圆环"——永远看不见怪凭空出现；
    /// ② 同屏上限 maxAliveEnemies 是帧率保护阀（宁可不刷，不能卡顿）；
    /// ③ 怪物组成随时间按 AnimationCurve 权重采样，一个资产管整局节奏。
    /// </summary>
    public class EnemyDirector : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private DifficultyCurve _difficulty;
        [SerializeField] private EnemyData _droneData;
        [SerializeField] private EnemyData _darterData;
        [SerializeField] private EnemyData _bruteData;
        [SerializeField] private Enemy _enemyPrefab;

        [Header("视野外生成圈半径")]
        [SerializeField] private float spawnRingRadius = 14f;

        private Transform _player;
        private float _spawnAccumulator;
        private float[] _weights; // 权重缓存数组（避免每次刷怪分配，保持零 GC 主循环）

        private readonly struct EnemyKind
        {
            public readonly EnemyData Data;
            public readonly AnimationCurve Weight;
            public EnemyKind(EnemyData data, AnimationCurve weight)
            {
                Data = data;
                Weight = weight;
            }
        }

        private readonly List<EnemyKind> _kinds = new();

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerController>().transform;
            _kinds.Add(new EnemyKind(_droneData, _difficulty.droneWeight));
            _kinds.Add(new EnemyKind(_darterData, _difficulty.darterWeight));
            _kinds.Add(new EnemyKind(_bruteData, _difficulty.bruteWeight));

            // 预热敌人池：开局瞬间不因实例化卡顿
            PoolService.Instance.Register(_enemyPrefab.gameObject, prewarm: 32, maxSize: 160);
        }

        private void Update()
        {
            if (GameManager.Instance.State != GameState.Playing) return;
            float minutes = GameManager.Instance.RunTime / 60f;

            // 同屏上限保护：超过就跳过本帧刷怪
            if (Enemy.AliveCount >= _difficulty.maxAliveEnemies) return;

            // 当前刷怪速率：开局→结束线性爬坡
            float rampT = Mathf.Clamp01(minutes / _difficulty.spawnRateRampMinutes);
            float rate = Mathf.Lerp(_difficulty.spawnRateStart, _difficulty.spawnRateEnd, rampT);

            _spawnAccumulator += rate * Time.deltaTime;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                SpawnOne(minutes);
            }
        }

        private void SpawnOne(float minutes)
        {
            // 按曲线权重选怪种（权重数组缓存在字段里，主循环零分配）
            float total = 0f;
            if (_weights == null || _weights.Length != _kinds.Count)
                _weights = new float[_kinds.Count];
            for (int i = 0; i < _kinds.Count; i++)
            {
                _weights[i] = Mathf.Max(0f, _kinds[i].Weight.Evaluate(minutes));
                total += _weights[i];
            }
            float roll = Random.value * total;
            EnemyKind picked = _kinds[_kinds.Count - 1];
            for (int i = 0; i < _kinds.Count; i++)
            {
                roll -= _weights[i];
                if (roll <= 0f) { picked = _kinds[i]; break; }
            }

            // 精英判定
            bool isElite = Random.value < _difficulty.eliteChance.Evaluate(minutes);

            // 玩家周围视野外圆环随机取点
            float angle = Random.value * Mathf.PI * 2f;
            Vector2 pos = (Vector2)_player.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spawnRingRadius;

            var go = PoolService.Instance.Spawn(_enemyPrefab.gameObject, pos, Quaternion.identity);
            go.GetComponent<Enemy>().Init(picked.Data, isElite, _player);
        }
    }
}
