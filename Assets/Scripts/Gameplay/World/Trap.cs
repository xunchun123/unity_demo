using StarVortex.Core;
using UnityEngine;

namespace StarVortex.Gameplay.World
{
    /// <summary>
    /// 陷阱组件集（SpikeTrap / FallingRock / FlameVent 共用一个文件）：
    /// 三者都是"纯触发器 + 行为组件"的薄组件，由 LevelDirector 按 LevelSpec 在运行期生成并注入视觉精灵。
    /// 面试点：陷阱只认 HeroController 组件不认层/标签——勇者 prefab 的层归属由其资产流程负责，
    /// 陷阱逻辑不与之耦合；伤害全部走 HeroController.TakeDamage（无敌帧/暂停状态由其内部拒绝），
    /// 陷阱自己不做状态判断，避免两套规则打架。
    /// 层数字契约：触发器挂 Ground(8) 层——碰撞矩阵里 Player↔Ground 开启，触发事件必然可达。
    /// </summary>
    public static class TrapLayers
    {
        public const int Ground = 8;
        public const int Enemy = 7;
    }

    // ============================================================ 尖刺 ============================================================

    /// <summary>
    /// 固定尖刺：Trigger 盒，勇者进入扣 15 血 + 向上小弹开，冷却 0.5s。
    /// 面试点："小弹开"是横版陷阱的关键手感——只把垂直速度顶起来、水平分量保留，
    /// 勇者不会被钉在尖刺上反复摩擦（水平击退交给 HeroController 自带的背向受击）。
    /// </summary>
    public class SpikeTrap : MonoBehaviour
    {
        [SerializeField] private float damage = 15f;
        [SerializeField] private float popVelocity = 6.5f;
        [SerializeField] private float cooldown = 0.5f;

        private float _cd;

        private void Update()
        {
            if (_cd > 0f) _cd -= Time.deltaTime;
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_cd > 0f) return;
            var hero = other.GetComponentInParent<HeroController>();
            if (hero == null || hero.IsDead) return;

            _cd = cooldown;
            hero.TakeDamage(new DamageInfo(damage, transform.position, false));

            var rb = hero.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = new Vector2(rb.velocity.x, popVelocity);
        }
    }

    // ============================================================ 落石 ============================================================

    /// <summary>
    /// 落石陷阱：勇者进入上方感应区 → 顶部生成 Box 刚体自由落体砸勇者，落地碎裂。
    /// 面试点：感应区用"从地面竖到生成点"的高触发柱而非薄板——勇者跳跃/冲刺穿越时
    /// 也一定命中触发，不会出现"贴地滑过不触发"的判定空洞；
    /// 石头挂 Enemy(7) 层复用碰撞矩阵：Enemy↔Ground 落地、Enemy↔Player 砸人，不用额外配置。
    /// </summary>
    public class FallingRock : MonoBehaviour
    {
        [Header("数值")]
        [SerializeField] private float damage = 25f;
        [SerializeField] private float rockSize = 0.9f;
        [SerializeField] private float gravityScale = 3f;
        [Tooltip("触发后的再武装时间（石头碎裂后多久可再次触发）")]
        [SerializeField] private float rearmDelay = 4f;

        [Header("生成（LevelDirector 注入）")]
        [Tooltip("石头在感应区根节点上方多少单位生成")]
        [SerializeField] private float spawnHeight = 4.4f;
        public Sprite rockSprite;
        public Color rockColor = new Color(0.78f, 0.72f, 0.74f, 1f); // 像素石块自带明暗，只轻微压暗

        private float _rearmTimer;

        private void Update()
        {
            if (_rearmTimer > 0f) _rearmTimer -= Time.deltaTime;
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_rearmTimer > 0f) return;
            var hero = other.GetComponentInParent<HeroController>();
            if (hero == null || hero.IsDead) return;

            _rearmTimer = rearmDelay;
            DropRock();
        }

        private void DropRock()
        {
            var rock = new GameObject("FallingRock_Body");
            rock.transform.position = transform.position + Vector3.up * spawnHeight;
            rock.layer = TrapLayers.Enemy;

            var rb = rock.AddComponent<Rigidbody2D>();
            rb.gravityScale = gravityScale;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // 高速下坠防穿地

            var col = rock.AddComponent<BoxCollider2D>();
            col.size = new Vector2(rockSize, rockSize);

            var sr = rock.AddComponent<SpriteRenderer>();
            sr.sprite = rockSprite; // 空 sprite 时只剩灰盒碰撞体也能玩（视觉代理兜底）
            sr.color = rockColor;
            sr.sortingOrder = 8;

            var body = rock.AddComponent<FallingRockRock>();
            body.Configure(damage, rockColor);
        }
    }

    /// <summary>落石实体：落地/砸中勇者后碎裂（FadeOutAndDie 模式的碎片 + Juice 迸发）。</summary>
    public class FallingRockRock : MonoBehaviour
    {
        private float _damage;
        private Color _color = Color.gray;
        private bool _hitHero;
        private float _life = 6f; // 兜底寿命：掉进坑里永远不落地也要回收

        public void Configure(float damage, Color color)
        {
            _damage = damage;
            _color = color;
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.collider.TryGetComponent<HeroController>(out var hero))
            {
                if (_hitHero || hero.IsDead) return;
                _hitHero = true;
                hero.TakeDamage(new DamageInfo(_damage, transform.position, false));
                Shatter();
                return;
            }
            // 碰到地形（Ground 层）→ 落地碎裂
            if (((1 << collision.collider.gameObject.layer) & (1 << TrapLayers.Ground)) != 0) Shatter();
        }

        /// <summary>碎裂：粒子迸发 + 三片 FadeOutAndDie 碎片（一次性视觉，淡出自毁）。</summary>
        private void Shatter()
        {
            if (Juice.Instance != null)
                Juice.DeathBurst(transform.position, _color, 12, 0.45f);

            var sr = GetComponent<SpriteRenderer>();
            for (int i = 0; i < 3; i++)
            {
                var shard = new GameObject("RockShard");
                shard.transform.position = transform.position + new Vector3(
                    Random.Range(-0.5f, 0.5f), Random.Range(0f, 0.4f), 0f);
                shard.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                shard.transform.localScale = Vector3.one * Random.Range(0.25f, 0.4f);
                var shardSr = shard.AddComponent<SpriteRenderer>();
                shardSr.sprite = sr != null ? sr.sprite : null;
                shardSr.color = new Color(_color.r * 0.8f, _color.g * 0.8f, _color.b * 0.8f);
                shardSr.sortingOrder = 7;
                // FadeOutAndDie 模式：先设色再加组件——其 Awake 缓存的就是碎裂色
                shard.AddComponent<FadeOutAndDie>().life = 0.35f;
            }
            Destroy(gameObject);
        }
    }

    // ============================================================ 喷火口 ============================================================

    /// <summary>
    /// 周期喷火口：每 2.5s 喷 0.6s，Trigger 区域内按节拍持续扣血。
    /// 面试点：① 喷发前 0.3s 预热提示（火口变亮 + 火苗探头）——周期陷阱没有预警就是纯背板；
    /// ② 区域伤害按 0.4s 节拍扣而非每帧扣——HeroController 的无敌帧本就挡住高频伤害，
    ///    陷阱侧再节流一次，伤害数字才可读、受击音效不会连成噪音。
    /// </summary>
    public class FlameVent : MonoBehaviour
    {
        [Header("周期")]
        [SerializeField] private float period = 2.5f;
        [SerializeField] private float burstDuration = 0.6f;
        [SerializeField] private float preGlow = 0.3f;

        [Header("伤害")]
        [SerializeField] private float damagePerTick = 8f;
        [SerializeField] private float tickInterval = 0.4f;

        [Header("视觉（LevelDirector 注入）")]
        public SpriteRenderer flameRenderer; // 火柱（喷发期显示）
        public SpriteRenderer ventRenderer;  // 火口底座（预热期变亮）
        [Tooltip("火柱是序列帧（SpriteLoop 播放）：不再拉伸缩放，只做亮度闪烁")]
        public bool pixelFrames;
        [Tooltip("预热/喷发时底座换成这张（栅缝透出火光）；为空则底座按颜色变亮")]
        public Sprite ventHotSprite;
        private Sprite _ventIdleSprite;

        private Color _ventBaseColor = Color.gray;
        private float _t;
        private float _tickTimer;
        private int _flickerSeed;

        /// <summary>当前周期是否处于喷发段（取周期末段喷发：休眠→预热→喷发的观感节奏）。</summary>
        private bool Erupting => _t >= period - burstDuration;
        private bool Warning => !Erupting && _t >= period - burstDuration - preGlow;

        private void Start()
        {
            _flickerSeed = GetInstanceID();
            if (ventRenderer != null)
            {
                _ventBaseColor = ventRenderer.color;
                _ventIdleSprite = ventRenderer.sprite;
            }
        }

        private void Update()
        {
            _t = (_t + Time.deltaTime) % period;
            UpdateVisual();
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (!Erupting) return;
            var hero = other.GetComponentInParent<HeroController>();
            if (hero == null || hero.IsDead) return;

            _tickTimer -= Time.deltaTime;
            if (_tickTimer > 0f) return;
            _tickTimer = tickInterval;
            hero.TakeDamage(new DamageInfo(damagePerTick, transform.position, false));
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            // 离开区域清零节拍：再次进入时第一时间吃到伤害（Stay 的"半拍进屋"不公平手感）
            if (other.GetComponentInParent<HeroController>() != null) _tickTimer = 0f;
        }

        private void UpdateVisual()
        {
            if (flameRenderer != null)
            {
                flameRenderer.gameObject.SetActive(Erupting);
                if (Erupting)
                {
                    // Perlin 闪烁：火苗高度/亮度抖动（Perlin 连续，比 Random 抖动自然）
                    float n = Mathf.PerlinNoise(Time.time * 18f, _flickerSeed * 0.01f);
                    // 像素帧自带动态，拉伸会让像素粗细不均；占位图才靠缩放模拟火苗
                    if (!pixelFrames) flameRenderer.transform.localScale = new Vector3(1.6f, 4.2f + n * 1.4f, 1f);
                    var c = flameRenderer.color;
                    flameRenderer.color = new Color(c.r, c.g, c.b, 0.75f + n * 0.25f);
                }
            }
            if (ventRenderer != null)
            {
                if (ventHotSprite != null)
                {
                    // 像素版预热：栅缝亮起火光（喷发全程保持），比整体染色更像"火要出来了"
                    ventRenderer.sprite = Warning || Erupting ? ventHotSprite : _ventIdleSprite;
                }
                else
                {
                    // 预热提示：底座向橙红过渡
                    float k = Warning ? 0.75f : 0f;
                    ventRenderer.color = Color.Lerp(_ventBaseColor, new Color(1f, 0.45f, 0.15f), k);
                }
            }
        }
    }
}
