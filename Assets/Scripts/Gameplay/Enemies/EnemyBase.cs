using StarVortex.Core;
using StarVortex.Gameplay;
using UnityEngine;

namespace StarVortex.Enemies
{
    /// <summary>
    /// 怪物基类契约（横版闯关版）：统一生命/受击/死亡流程，具体 AI 由子类实现。
    /// 面试点：模板方法模式——TakeDamage 固定了"闪白→击退钩子→死亡结算"的骨架，
    /// 子类只覆写行为钩子，受击表现不会因怪物种类而走样。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class EnemyBase : MonoBehaviour, IDamageable
    {
        [Header("基础数值")]
        [SerializeField] protected float maxHp = 30f;
        [SerializeField] protected int xpValue = 5;
        [SerializeField] protected float contactDamage = 10f;
        [Tooltip("每深入一关血量增加的比例（第 2 关 ×1.3、第 3 关 ×1.6）；BOSS 填 0，它的血量是单独设计的")]
        [SerializeField] protected float hpPerLevel = 0.3f;

        [Header("受击手感")]
        [Tooltip("受击硬直秒数：期间 AI 暂停、击退生效。精英/BOSS 填 0（霸体，只闪光不硬直）")]
        [SerializeField] protected float staggerTime = 0.22f;
        [Tooltip("击退抗性 0~1：体型越大越难推动")]
        [SerializeField, Range(0f, 1f)] protected float knockbackResist = 0f;
        [Tooltip("头顶血条宽度（世界单位）；0 = 不显示（BOSS 用屏幕顶部的大血条）")]
        [SerializeField] protected float hpBarWidth = 0.8f;
        [Tooltip("纯色剪影材质（GUI/Text Shader）：受击闪光与血条共用，构建器注入")]
        [SerializeField] protected Material _flatMaterial;

        [Header("像素动画（构建器注入）")]
        [Tooltip("Visual 子物体上的 Animator：只写 Speed / Windup / Attack 三个参数，sprite 由它独占")]
        [SerializeField] protected Animator _anim;
        [SerializeField] private Sprite[] _deathFrames;   // 死亡爆散序列帧（SpriteFx 播放）
        [SerializeField] private float _deathFxScale = 1f;

        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimWindupParam = Animator.StringToHash("Windup");
        private static readonly int AnimAttackParam = Animator.StringToHash("Attack");

        protected float _hp;
        protected float _maxHpRuntime;    // 本关实际血量上限（maxHp × 关卡系数）
        protected float _staggerTimer;
        // 同帧去重：怪物有两个碰撞体（实体 + 接触伤害 Trigger），范围判定会把同一只怪扫到两次，
        // 旧版每一刀实际打出双倍伤害——这也是"怪一下就没了"的另一半原因
        private int _lastHitFrame = -1;
        private float _lastHitAmount;
        private Vector2 _lastHitSource;
        private SpriteFlash _flash;
        private EnemyHealthBar _hpBar;
        private ProceduralAnim _pa;
        protected Rigidbody2D _rb;
        protected SpriteRenderer _sr;
        protected BoxCollider2D _body;     // 实体碰撞体（非 Trigger）：技能判定盒按它的尺寸摆，换体型不用改 AI 常数
        protected Transform _hero;
        protected bool _facingRight = true;

        public bool IsDead { get; protected set; }
        public float ContactDamage => contactDamage;

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _sr = GetComponentInChildren<SpriteRenderer>();
            if (_anim == null) _anim = GetComponentInChildren<Animator>();
            foreach (var col in GetComponents<BoxCollider2D>())
                if (!col.isTrigger) { _body = col; break; }
            _pa = GetComponent<ProceduralAnim>();
            _flash = SpriteFlash.Attach(_sr, _flatMaterial);
            if (hpBarWidth > 0f)
            {
                float top = _body != null ? _body.offset.y + _body.size.y * 0.5f : 1f;
                _hpBar = EnemyHealthBar.Create(transform, hpBarWidth, top + 0.28f, _flatMaterial);
            }
        }

        /// <summary>当前血量比例（0~1）。</summary>
        public float HpRatio => _maxHpRuntime > 0f ? Mathf.Clamp01(_hp / _maxHpRuntime) : 0f;

        /// <summary>身体中心（世界坐标）：弹道出生点、环形弹幕圆心等按它算。</summary>
        protected Vector2 BodyCenter => _body != null ? (Vector2)_body.bounds.center : (Vector2)transform.position;

        /// <summary>身体半宽：近战判定盒、撞墙探测放在身体前沿之外。</summary>
        protected float BodyHalfWidth => _body != null ? _body.bounds.extents.x : 0.4f;

        /// <summary>进入 / 离开前摇（动画停在前摇最后一帧，直到 AI 出手或被打断）。</summary>
        protected void AnimWindup(bool on)
        {
            if (_anim != null) _anim.SetBool(AnimWindupParam, on);
        }

        /// <summary>出手：结束前摇并播放攻击动作。</summary>
        protected void AnimAttack()
        {
            if (_anim == null) return;
            _anim.SetBool(AnimWindupParam, false);
            _anim.SetTrigger(AnimAttackParam);
        }

        /// <summary>由关卡引导器注入勇者引用并复位状态（池化复用入口）。</summary>
        public virtual void Init(Transform hero)
        {
            _hero = hero;
            int level = GameManager.Instance != null ? GameManager.Instance.CurrentLevel : 1;
            _maxHpRuntime = maxHp * (1f + hpPerLevel * Mathf.Max(0, level - 1));
            _hp = _maxHpRuntime;
            _staggerTimer = 0f;
            IsDead = false;
            _hpBar?.ResetFull();
            if (_sr != null) _sr.color = Color.white;
            if (_anim != null)
            {
                // 池化复用：参数和状态都回到初始值（上次可能死在前摇中，Windup 还是 true）
                _anim.Rebind();
                _anim.Update(0f);
            }
        }

        protected virtual void Update()
        {
            if (_hero == null || IsDead) return;
            // 坠出关卡底部（断桥/悬崖）：直接死亡结算，防止怪物永坠占用池与计数
            if (transform.position.y < -6f) { Die(); return; }

            // 受击硬直：AI 暂停一小会儿，让击退真正把怪推开（否则 AI 每帧重写速度，击退被吃掉）
            if (_staggerTimer > 0f)
            {
                _staggerTimer -= Time.deltaTime;
                if (_rb != null)
                {
                    float vx = Mathf.MoveTowards(_rb.velocity.x, 0f, 14f * Time.deltaTime);
                    float vy = _rb.gravityScale == 0f ? Mathf.MoveTowards(_rb.velocity.y, 0f, 14f * Time.deltaTime) : _rb.velocity.y;
                    _rb.velocity = new Vector2(vx, vy);
                }
                return;
            }

            _facingRight = _hero.position.x > transform.position.x;
            Behaviour();
            if (_anim != null && _rb != null) _anim.SetFloat(AnimSpeed, Mathf.Abs(_rb.velocity.x));
        }

        /// <summary>子类 AI 行为主体（每帧调用，勇者存在且未死亡时）。</summary>
        protected abstract void Behaviour();

        // ---------- IDamageable ----------

        public virtual void TakeDamage(in DamageInfo damage)
        {
            if (IsDead) return;
            if (Time.frameCount == _lastHitFrame && Mathf.Approximately(damage.Amount, _lastHitAmount)
                && (damage.SourcePosition - _lastHitSource).sqrMagnitude < 0.0001f)
                return; // 同一次攻击扫到了同一只怪的第二个碰撞体
            _lastHitFrame = Time.frameCount;
            _lastHitAmount = damage.Amount;
            _lastHitSource = damage.SourcePosition;

            _hp -= damage.Amount;
            _hpBar?.SetRatio(HpRatio);

            // 受击三件套：白色剪影闪光 + 视觉抖动 + （小怪）硬直击退
            _flash?.Play(new Color(1f, 1f, 1f, 0.95f), 0.09f);
            float side = Mathf.Sign(transform.position.x - damage.SourcePosition.x);
            if (side == 0f) side = 1f;
            _pa?.NotifyHit(side);
            if (staggerTime > 0f) _staggerTimer = Mathf.Max(_staggerTimer, staggerTime);
            // 命中爆点：白核+金晕在武器接触点（攻受双方连线中点）炸开——近战/弹丸/灼烧统一吃到
            Vector2 fromSource = (Vector2)transform.position - damage.SourcePosition;
            Vector3 hitPos = fromSource.sqrMagnitude > 0.0001f
                ? (transform.position + new Vector3(damage.SourcePosition.x, damage.SourcePosition.y, transform.position.z)) * 0.5f
                  + Vector3.up * 0.35f
                : transform.position + Vector3.up * 0.4f;
            Juice.ImpactSpark(hitPos);
            DamagePop.Spawn(transform.position + Vector3.up * 0.5f, Mathf.RoundToInt(damage.Amount), damage.IsCrit);
            // 击退以水平为主、略带上挑：纯按连线方向推，勇者在高处时怪会被砸进地里
            float force = (damage.IsCrit ? 6f : 4f) * (1f - knockbackResist);
            if (force > 0.05f) ApplyKnockback(new Vector2(side, 0.35f).normalized, force);

            if (_hp <= 0f) Die();
        }

#if UNITY_EDITOR
        /// <summary>编辑器调试：击杀离勇者最近的怪物（自动化试玩用它验证死亡特效与掉落）。</summary>
        public static string DebugKillNearestToHero()
        {
            var hero = FindFirstObjectByType<HeroController>();
            if (hero == null) return "no hero";
            EnemyBase nearest = null;
            float best = float.MaxValue;
            foreach (var enemy in FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
            {
                if (enemy.IsDead) continue;
                float d = Vector2.Distance(enemy.transform.position, hero.transform.position);
                if (d < best) { best = d; nearest = enemy; }
            }
            if (nearest == null) return "no enemy";
            nearest._hp = 0f;
            nearest.Die();
            return $"{nearest.name} @ {best:0.0}u";
        }

        /// <summary>编辑器调试：离勇者最近的怪物的血量（验证"几下打死"）。</summary>
        public static string DebugNearestHp()
        {
            var hero = FindFirstObjectByType<HeroController>();
            if (hero == null) return "no hero";
            EnemyBase nearest = null;
            float best = float.MaxValue;
            foreach (var enemy in FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
            {
                if (enemy.IsDead) continue;
                float d = Vector2.Distance(enemy.transform.position, hero.transform.position);
                if (d < best) { best = d; nearest = enemy; }
            }
            return nearest == null ? "no enemy" : $"{nearest.name} {nearest._hp:0}/{nearest._maxHpRuntime:0} @ {best:0.0}u";
        }
#endif

        /// <summary>击退（冲击波/震地等大范围效果调用；子类按自身运动学实现，飞行怪与地面怪不同）。</summary>
        public abstract void ApplyKnockback(Vector2 dir, float force);

        /// <summary>冻结（冰霜新星爽点）：冻结期间跳过 AI 行为、velocity 清零。子类在其运动逻辑里检查 _frozenTimer。</summary>
        public virtual void SetFrozen(float seconds) { }

        private static readonly Color XpOrbGold = new Color(1.6f, 1.35f, 0.4f, 1f); // 经验珠金光（HDR 喂 Bloom）

        protected virtual void Die()
        {
            if (IsDead) return;
            IsDead = true;
            _hpBar?.SetRatio(0f);
            int xp = xpValue;
            GameEvents.PublishEnemyDied(xp, transform.position);
            GameManager.Instance.AddKill();
            // 像素爆散序列帧（素材自带）+ 少量粒子碎片补动感
            SpriteFx.Spawn(_deathFrames, BodyCenter, 16f, _deathFxScale, _sr != null ? _sr.sortingOrder + 1 : 30);
            Juice.DeathBurst(BodyCenter, _sr != null ? _sr.color : Color.white, _deathFrames != null && _deathFrames.Length > 0 ? 12 : 26, 0.85f);
            // 经验珠视觉：金光珠缩放弹入+上飘淡出（经验数值链路不动，只补"进账"的视觉）
            Juice.XpOrbPop(transform.position + Vector3.up * 0.5f, XpOrbGold);
            AudioService.Instance?.Play(SfxId.Kill);
            OnDeath();
        }

        /// <summary>死亡后的清理（回到池/爆点特效/掉落），子类实现。</summary>
        protected abstract void OnDeath();

        /// <summary>接触伤害触发器：与勇者重叠时按冷却扣血（钩子：需要特殊接触行为的怪覆写）。</summary>
        protected float _contactTimer;
        protected virtual void OnTriggerStay2D(Collider2D other)
        {
            if (IsDead) return;
            _contactTimer -= Time.deltaTime;
            if (_contactTimer > 0f) return;
            if (!other.TryGetComponent<HeroController>(out var hero)) return;
            _contactTimer = 0.8f;
            hero.TakeDamage(new DamageInfo(contactDamage, transform.position, false));
        }
    }
}
