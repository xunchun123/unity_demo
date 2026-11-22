using TMPro;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 伤害飘字：命中数字向上飘 + 随机弧度 + 淡出，暴击放大变金。
    /// 面试点：飘字也走对象池 + world space Canvas 一次烘焙，
    /// 每次命中零实例化开销；UI 数字用 TMP（SDF 字体渲染，任意缩放不糊）。
    /// </summary>
    public class DamagePop : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private float riseSpeed = 1.6f;
        [SerializeField] private float lifeTime = 0.55f;

        private static readonly Color NormalColor = new Color(0.92f, 0.95f, 1f, 1f);
        private static readonly Color CritColor = new Color(1.7f, 1.45f, 0.35f, 1f); // HDR 金，喂 Bloom

        private float _timer;
        private Vector2 _drift;
        private float _scale;

        /// <summary>统一入口（内部走池）。</summary>
        public static void Spawn(Vector3 pos, int amount, bool isCrit)
        {
            var pop = PopPool.Instance?.Spawn(pos);
            if (pop == null) return;
            pop.Init(amount, isCrit);
        }

        private void Init(int amount, bool isCrit)
        {
            _timer = lifeTime;
            _drift = new Vector2(Random.Range(-0.5f, 0.5f), 1f);
            _scale = isCrit ? 1.45f : 1f;
            _label.text = amount.ToString();
            _label.color = isCrit ? CritColor : NormalColor;
            _label.fontSize = isCrit ? 4.2f : 3f;
            transform.localScale = Vector3.one * (isCrit ? 1.2f : 0.9f);
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) { PopPool.Instance.Despawn(this); return; }

            transform.position += (Vector3)(_drift * (riseSpeed * Time.deltaTime));
            // 后半段淡出（alpha 乘回去）
            float k = _timer / lifeTime;
            var c = _label.color;
            c.a = Mathf.Clamp01(k * 1.4f);
            _label.color = c;
        }
    }

    /// <summary>飘字池（世界空间文本池）。</summary>
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
