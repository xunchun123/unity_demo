using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 经验宝石：待机漂浮 → 玩家拾取圈触发磁吸 → 加速飞向玩家 → 拾取。
    /// 面试点：磁吸手感是"廉价改动、巨大体验提升"的典型——
    /// 触发器进入即吸附 + 越吸越快，玩家会主动想去"扫图"；
    /// 宝石本身不带刚体，触发检测依赖玩家侧 Rigidbody2D，省一倍物理开销。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class XPGem : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _sr;
        [SerializeField] private float attractSpeedStart = 6f;
        [SerializeField] private float attractAccel = 34f;

        private int _xpValue;
        private Transform _player;
        private bool _attracted;
        private float _speed;
        private float _bobPhase;

        /// <summary>统一入口：敌人死亡调这里（内部走对象池）。</summary>
        public static void SpawnAt(Vector3 position, int xp)
        {
            if (GemPool.Instance == null) return;
            var gem = GemPool.Instance.Spawn(position);
            gem.Init(xp);
        }

        /// <summary>从池中取出后的初始化（公开给测试与池使用）。</summary>
        public void Init(int xp) => _xpValue = xp;

        private void Awake() => _bobPhase = Random.value * Mathf.PI * 2f;

        private void OnEnable()
        {
            // 池对象复用必须全量重置状态（漏掉 _attracted 会让新宝石直线冲向旧目标）
            _attracted = false;
            _player = null;
            _speed = attractSpeedStart;
            _bobPhase = Random.value * Mathf.PI * 2f;
        }

        private void Update()
        {
            if (GameManager.Instance.State != GameState.Playing) return;

            // 待机漂浮：正弦浮动 + 微旋转（廉价但画面"活"）
            _bobPhase += Time.deltaTime * 3f;
            _sr.transform.localPosition = new Vector3(0f, Mathf.Sin(_bobPhase) * 0.06f, 0f);
            _sr.transform.rotation = Quaternion.Euler(0f, 0f, _bobPhase * 40f);

            // 磁吸飞行（触发器已标记吸附状态）
            if (_attracted)
            {
                if (TryCachePlayer())
                {
                    Vector3 toPlayer = _player.position - transform.position;
                    _speed += attractAccel * Time.deltaTime;
                    transform.position += toPlayer.normalized * (_speed * Time.deltaTime);
                    if (toPlayer.sqrMagnitude < 0.16f) Collect();
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 进入玩家拾取圈 → 开始磁吸（拾取圈是 Player 子物体上的触发器，尺寸随升级变化）
            if (other.CompareTag("Player")) _attracted = true;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.CompareTag("Player")) _attracted = false;
        }

        private bool TryCachePlayer()
        {
            if (_player != null) return true;
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc == null || pc.IsDead) return false;
            _player = pc.transform;
            return true;
        }

        private void Collect()
        {
            _player = null;
            _attracted = false;
            Juice.PickupSpark(transform.position, _sr.color);
            AudioService.Instance?.Play(SfxId.Pickup);
            FindFirstObjectByType<PlayerController>().AddXp(_xpValue); // 升级流转由 GameManager 接管
            GemPool.Instance.Despawn(this);
        }
    }

    /// <summary>
    /// 宝石专用池：宝石没有 SO 资产，用一个小单例持有预制体。
    /// （宝石数量大、生命周期短，独立池比通用注册表更直观）
    /// </summary>
    public class GemPool : MonoBehaviour
    {
        public static GemPool Instance { get; private set; }
        [SerializeField] private XPGem _gemPrefab;
        private UnityEngine.Pool.ObjectPool<XPGem> _pool;

        private void Awake()
        {
            Instance = this;
            _pool = new UnityEngine.Pool.ObjectPool<XPGem>(
                () => Instantiate(_gemPrefab),
                g => g.gameObject.SetActive(true),
                g => g.gameObject.SetActive(false),
                g => Destroy(g.gameObject),
                collectionCheck: Application.isEditor,
                defaultCapacity: 96, maxSize: 240);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public XPGem Spawn(Vector3 position)
        {
            var gem = _pool.Get();
            gem.transform.position = position;
            return gem;
        }

        public void Despawn(XPGem gem) => _pool.Release(gem);
    }
}
