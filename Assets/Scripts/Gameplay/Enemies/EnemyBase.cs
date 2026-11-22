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

        protected float _hp;
        protected Rigidbody2D _rb;
        protected SpriteRenderer _sr;
        protected Transform _hero;
        protected bool _facingRight = true;

        public bool IsDead { get; protected set; }
        public float ContactDamage => contactDamage;

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _sr = GetComponentInChildren<SpriteRenderer>();
        }

        /// <summary>由关卡引导器注入勇者引用并复位状态（池化复用入口）。</summary>
        public virtual void Init(Transform hero)
        {
            _hero = hero;
            _hp = maxHp;
            IsDead = false;
            if (_sr != null) _sr.color = Color.white;
        }

        protected virtual void Update()
        {
            if (_hero == null || IsDead) return;
            // 坠出关卡底部（断桥/悬崖）：直接死亡结算，防止怪物永坠占用池与计数
            if (transform.position.y < -6f) { Die(); return; }
            _facingRight = _hero.position.x > transform.position.x;
            Behaviour();
        }

        /// <summary>子类 AI 行为主体（每帧调用，勇者存在且未死亡时）。</summary>
        protected abstract void Behaviour();

        // ---------- IDamageable ----------

        public virtual void TakeDamage(in DamageInfo damage)
        {
            if (IsDead) return;
            _hp -= damage.Amount;

            Juice.Flash(_sr, Color.white);
            // 命中爆点：白核+金晕在武器接触点（攻受双方连线中点）炸开——近战/弹丸/灼烧统一吃到
            Vector2 fromSource = (Vector2)transform.position - damage.SourcePosition;
            Vector3 hitPos = fromSource.sqrMagnitude > 0.0001f
                ? (transform.position + new Vector3(damage.SourcePosition.x, damage.SourcePosition.y, transform.position.z)) * 0.5f
                  + Vector3.up * 0.35f
                : transform.position + Vector3.up * 0.4f;
            Juice.ImpactSpark(hitPos);
            DamagePop.Spawn(transform.position + Vector3.up * 0.5f, Mathf.RoundToInt(damage.Amount), damage.IsCrit);
            Vector2 away = ((Vector2)transform.position - damage.SourcePosition).normalized;
            ApplyKnockback(away, damage.IsCrit ? 5f : 3f);

            if (_hp <= 0f) Die();
        }

        /// <summary>击退（冲击波/震地等大范围效果调用；子类按自身运动学实现，飞行怪与地面怪不同）。</summary>
        public abstract void ApplyKnockback(Vector2 dir, float force);

        /// <summary>冻结（冰霜新星爽点）：冻结期间跳过 AI 行为、velocity 清零。子类在其运动逻辑里检查 _frozenTimer。</summary>
        public virtual void SetFrozen(float seconds) { }

        private static readonly Color XpOrbGold = new Color(1.6f, 1.35f, 0.4f, 1f); // 经验珠金光（HDR 喂 Bloom）

        protected virtual void Die()
        {
            if (IsDead) return;
            IsDead = true;
            int xp = xpValue;
            GameEvents.PublishEnemyDied(xp, transform.position);
            GameManager.Instance.AddKill();
            // 死亡视觉加大：碎片 14→26、半径 0.5→0.85（DeathBurst 的初速/尺寸都随半径放大）
            Juice.DeathBurst(transform.position, _sr != null ? _sr.color : Color.white, 26, 0.85f);
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
