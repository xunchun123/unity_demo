using StarVortex.Core;
using StarVortex.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 玩家控制器：移动、生命、经验拾取、暴击规则。
    /// 面试点：移动用 Rigidbody2D.velocity 在 FixedUpdate 里驱动（物理系统权威），
    /// 而不是直接改 Transform——保证与敌人碰撞检测的一致性，也是面试常问点。
    /// 输入用新版 Input System 的 Keyboard 轮询，同时支持手柄左摇杆。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour, IDamageable
    {
        [Header("组件引用")]
        [SerializeField] private Rigidbody2D _rb;
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private WeaponController _weapon;
        [Tooltip("拾取圈触发器（半径随升级实时变化）")]
        [SerializeField] private CircleCollider2D _pickupArea;
        [Tooltip("角色动画控制器（Idle/Run 切换）")]
        [SerializeField] private Animator _animator;

        [Header("受击表现")]
        [Tooltip("受伤无敌帧（秒）——防止同帧多次接触伤害直接秒杀")]
        [SerializeField] private float invulnerableDuration = 0.6f;
        [Tooltip("受伤闪红强度")]
        [SerializeField] private Color hurtColor = new Color(1f, 0.25f, 0.3f, 1f);

        public PlayerStats Stats { get; private set; }
        public float CurrentHp { get; private set; }
        public bool IsDead => CurrentHp <= 0f;

        /// <summary>出生配置（由 GameScene 的启动器注入武器资产等）。</summary>
        [SerializeField] private WeaponData _startingWeapon;
        public WeaponData StartingWeapon => _startingWeapon;

        // 受击无敌帧计时
        private float _invulnTimer;
        private static readonly Color NormalColor = Color.white;

        private void Reset() // 组件挂载时自动抓引用，减少手工配置
        {
            _rb = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void Awake()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            // 刚体参数：高响应的顶视角移动
            _rb.gravityScale = 0f;
            _rb.drag = 0f;
            _rb.angularDrag = 0f;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        private void Start()
        {
            Stats = new PlayerStats();
            CurrentHp = Stats.MaxHp;
            GameEvents.PublishPlayerHpChanged(CurrentHp, Stats.MaxHp);
            GameEvents.PublishXpChanged(Stats.Xp, Stats.XpToNext);
            if (_weapon != null && _startingWeapon != null) _weapon.Equip(_startingWeapon);
        }

        private void Update()
        {
            if (GameManager.Instance.State != GameState.Playing) return;
            if (_invulnTimer > 0f)
            {
                _invulnTimer -= Time.deltaTime;
                // 无敌帧期间呼吸闪烁（青白交替，提示玩家当前免伤）
                bool blink = Mathf.PingPong(Time.time * 12f, 1f) > 0.5f;
                _spriteRenderer.color = blink ? hurtColor : NormalColor;
                if (_invulnTimer <= 0f) _spriteRenderer.color = NormalColor;
            }

            // 回血
            if (Stats.hpRegen > 0f && CurrentHp < Stats.MaxHp && !IsDead)
            {
                CurrentHp = Mathf.Min(Stats.MaxHp, CurrentHp + Stats.hpRegen * Time.deltaTime);
                GameEvents.PublishPlayerHpChanged(CurrentHp, Stats.MaxHp);
            }

            // 拾取圈半径跟随升级变化（引力线圈升级立刻生效的视觉反馈）
            if (_pickupArea != null && !Mathf.Approximately(_pickupArea.radius, Stats.PickupRadius))
                _pickupArea.radius = Stats.PickupRadius;

            // 动画状态：按实际速度驱动 Idle/Run 切换（阈值留死区防抖动）
            if (_animator != null)
            {
                bool moving = _rb.velocity.sqrMagnitude > 0.3f;
                if (_animator.GetBool("Moving") != moving)
                    _animator.SetBool("Moving", moving);
            }
        }

        private void FixedUpdate()
        {
            if (GameManager.Instance.State != GameState.Playing) { _rb.velocity = Vector2.zero; return; }
            _rb.velocity = ReadMoveInput() * (Stats.MoveSpeed);
        }

        /// <summary>读取移动输入：键盘 WASD/方向键 + 手柄左摇杆，归一化防斜向加速。</summary>
        private Vector2 ReadMoveInput()
        {
            Vector2 v = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
            }
            var pad = Gamepad.current;
            if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.04f) v += pad.leftStick.ReadValue();
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        // ---------- IDamageable ----------

        public void TakeDamage(in DamageInfo damage)
        {
            if (IsDead || _invulnTimer > 0f) return;
            if (GameManager.Instance.State != GameState.Playing) return;

            CurrentHp -= damage.Amount;
            _invulnTimer = invulnerableDuration;
            GameEvents.PublishPlayerHpChanged(CurrentHp, Stats.MaxHp);
            GameEvents.PublishPlayerHurt(damage.SourcePosition);

            Juice.HitStop(0.05f);
            CameraRig.Instance?.AddTrauma(0.35f);
            AudioService.Instance?.Play(SfxId.PlayerHurt);

            if (CurrentHp <= 0f)
            {
                CurrentHp = 0f;
                GameEvents.PublishPlayerHpChanged(0f, Stats.MaxHp);
                GameManager.Instance.HandlePlayerDeath();
            }
        }

        /// <summary>治疗（升级 MaxHp 时同时补足差额）。</summary>
        public void Heal(float amount)
        {
            if (IsDead) return;
            CurrentHp = Mathf.Min(Stats.MaxHp, CurrentHp + amount);
            GameEvents.PublishPlayerHpChanged(CurrentHp, Stats.MaxHp);
        }

        // ---------- 经验 ----------

        /// <summary>由 XPGem 调用。返回 true 表示触发了升级（宝石据此停止吸附）。</summary>
        public bool AddXp(int amount)
        {
            if (IsDead) return false;
            int levelUps = Stats.AddXp(amount);
            GameEvents.PublishXpChanged(Stats.Xp, Stats.XpToNext);
            if (levelUps > 0)
            {
                GameEvents.PublishLevelUp(Stats.Level);
                GameManager.Instance.RequestLevelUps(levelUps);
                return true;
            }
            return false;
        }
    }
}
