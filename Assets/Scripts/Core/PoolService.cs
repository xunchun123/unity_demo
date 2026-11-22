using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace StarVortex.Core
{
    /// <summary>
    /// 对象池服务：管理所有需要频繁生成/回收的对象（子弹、敌人、宝石、飘字、粒子）。
    /// 面试点：割草类每局要生成上千个对象，Instantiate/Destroy 会造成 GC 尖峰和卡顿；
    /// 这里基于引擎自带的 UnityEngine.Pool.ObjectPool（2021 起官方内置）做统一注册表，
    /// collectionCheck 仅在 Editor 下开启（发布版关闭，省一次判定的开销）。
    /// </summary>
    public class PoolService : MonoBehaviour
    {
        public static PoolService Instance { get; private set; }

        /// <summary>池的注册表：预制体 → 对应对象池。</summary>
        private readonly Dictionary<GameObject, IObjectPool<GameObject>> _pools = new();

        /// <summary>实例 → 归属预制体的反查表（池对象复用，键数量稳定不膨胀）。</summary>
        private readonly Dictionary<GameObject, GameObject> _instanceToPrefab = new();

        /// <summary>所有池对象统一的父节点，防止 Hierarchy 被刷爆、便于整体隐藏。</summary>
        private Transform _poolRoot;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>懒初始化池根节点（PoolService 跨场景常驻，场景切换后仍有效）。</summary>
        private void EnsureInitialized()
        {
            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("~Pools").transform;
                _poolRoot.SetParent(transform);
            }
        }

        /// <summary>注册一个池：指定预制体、预热水位、容量上限。</summary>
        public void Register(GameObject prefab, int prewarm = 16, int maxSize = 512)
        {
            if (_pools.ContainsKey(prefab)) return;
            EnsureInitialized();

            var pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = Instantiate(prefab);
                    go.name = prefab.name; // 去掉 "(Clone)"，Hierarchy 干净
                    return go;
                },
                actionOnGet: go => go.SetActive(true),
                actionOnRelease: go => go.SetActive(false),
                actionOnDestroy: go => Destroy(go),
                collectionCheck: Application.isEditor,
                defaultCapacity: prewarm,
                maxSize: maxSize);

            // 预热：提前实例化填满池，避免开局卡顿（把 Instantiate 成本挪到加载期）
            var prewarmed = new Stack<GameObject>();
            for (int i = 0; i < prewarm; i++) prewarmed.Push(pool.Get());
            while (prewarmed.Count > 0) pool.Release(prewarmed.Pop());

            _pools[prefab] = pool;
        }

        /// <summary>从池里取一个实例（必要时自动扩容）。</summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!_pools.ContainsKey(prefab)) Register(prefab);
            var go = _pools[prefab].Get();
            go.transform.SetPositionAndRotation(position, rotation);
            _instanceToPrefab[go] = prefab;
            return go;
        }

        /// <summary>归还实例到池（自动反查归属预制体）。</summary>
        public void Despawn(GameObject instance)
        {
            if (_instanceToPrefab.TryGetValue(instance, out var prefab) &&
                _pools.TryGetValue(prefab, out var pool))
            {
                pool.Release(instance);
            }
            else
            {
                Destroy(instance); // 未走池的兜底直接销毁
            }
        }
    }
}
