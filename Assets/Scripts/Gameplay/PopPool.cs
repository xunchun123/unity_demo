using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>飘字池（世界空间文本池）。见 GemPool.cs 顶部的"次类 fileID"教训，必须独立成文件。</summary>
    public class PopPool : MonoBehaviour
    {
        public static PopPool Instance { get; private set; }
        [SerializeField] private DamagePop _popPrefab;
        private UnityEngine.Pool.ObjectPool<DamagePop> _pool;

        private void Awake()
        {
            Instance = this;
            _pool = new UnityEngine.Pool.ObjectPool<DamagePop>(
                () => Instantiate(_popPrefab),
                p => p.gameObject.SetActive(true),
                p => p.gameObject.SetActive(false),
                p => Destroy(p.gameObject),
                collectionCheck: Application.isEditor,
                defaultCapacity: 48, maxSize: 120);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public DamagePop Spawn(Vector3 position)
        {
            var pop = _pool.Get();
            pop.transform.position = position;
            return pop;
        }

        public void Despawn(DamagePop pop) => _pool.Release(pop);
    }
}
