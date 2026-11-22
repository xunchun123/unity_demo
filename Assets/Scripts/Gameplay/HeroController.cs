using StarVortex.Core;
using StarVortex.Enemies;
using StarVortex.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 勇者控制器（横版闯关版）：平台物理 + 三武器攻击形态 + 天赋/爽点成长钩子。
    /// 面试点：
    /// ① 平台跳跃手感三件套——土狼时间（离开平台仍可跳）+ 跳跃缓冲（落地前按跳也生效）+ 二段跳，
    ///    三者缺一手感就"硬"；② 攻击形态由 WeaponKind 分派，爽点系统用 BoonEffects 钩子注入行为，
    ///    武器强化树互不污染（长剑的剑气永远影响不到法杖）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class HeroController : MonoBehaviour, IDamageable
    {
        [Header("组件")]
        [SerializeField] private Rigidbody2D _rb;
        [SerializeField] private SpriteRenderer _sr;
        [SerializeField] private Transform _attackOrigin; // 攻击判定锚点（身体前侧）
        [Tooltip("动画状态机（Idle/Run 切换，由构建器程序化创建）")]
        [SerializeField] private Animator _animator;

        [Header("武器（选人界面注入）")]
        [SerializeField] private WeaponKit _weaponKit;
        public WeaponKind Kind => _weaponKit != null ? _weaponKit.kind : WeaponKind.Sword;

        [Header("帧动画（用户 AI 生图帧，代码驱动换帧——Animator PPtr 曲线在 Player 端不生效的兜底）")]
        [SerializeField] private Sprite[] _idleFrames;    // 站姿单帧
        [SerializeField] private Sprite[] _runFrames;     // 迈步→收拢→迈步→收拢 四拍循环
        [SerializeField] private Sprite[] _jumpFrames;    // 上升收腿
        [SerializeField] private Sprite[] _fallFrames;    // 下落展腿
        [SerializeField] private Sprite[] _attackFrames;  // 蓄力→突刺→收招
        private float _runFrameTimer;
        private int _runFrameIndex;

        [Header("平台参数")]
        [SerializeField] private float jumpVelocity = 12.2f; // 满跳峰值 ≈2.23u（台阶每级 1u：点按跳 1.4u 也能上，按住跳更从容）
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private float jumpBufferTime = 0.12f;
        [SerializeField] private LayerMask groundMask = 1 << 8;
        // GitHub 调研（Brackeys/BoardToBits/SuperSimple2DKit 共识参数）：
        [Tooltip("下落重力倍率——下坠比上升快，跳跃更'脆'")]
        [SerializeField] private float fallMultiplier = 2.5f;
        [Tooltip("上升中松开跳跃键时的重力倍率——实现'松键提前下落'的可变跳跃高度（2→1.6：点按跳峰值须 > 台阶 1u，否则点按玩家被 1u 台阶卡死）")]
        [SerializeField] private float lowJumpMultiplier = 1.6f;
        [Tooltip("跳跃顶点附近重力衰减——apex 悬停让操作窗口更宽")]
        [SerializeField] private float apexMultiplier = 0.6f;

        [Header("冲刺")]
        [SerializeField] private float dashSpeed = 15f;
        [SerializeField] private float dashDuration = 0.15f;
        [SerializeField] private float dashCooldown = 1.2f;
        [SerializeField] private float dashInvuln = 0.25f;

        [Header("火球（法杖）")]
        [SerializeField] private float fireballManaCost = 25f;
        [SerializeField] private Fireball _fireballPrefab;

        [Header("受击")]
        [Tooltip("受伤无敌帧——GitHub 两仓库共同值 0.8-1.0s")]
        [SerializeField] private float invulnerableDuration = 0.8f;
        [SerializeField] private Color hurtColor = new Color(1f, 0.3f, 0.35f, 1f);

        public HeroStats Stats { get; } = new();
        public BoonInventory Boons { get; } = new();
        public float CurrentHp { get; private set; }
        public float CurrentMana { get; private set; }
        public bool IsDead => CurrentHp <= 0f;
        public bool DashReady => _dashCdTimer <= 0f;

        // 朝向：写入时同步翻转贴图——此前是自动属性，全工程没有任何代码碰过 flipX，
        // "向左移动人物不面向左"的根因（素材原生面右，flipX=false=朝右）
        private bool _facingRight = true;
        public bool FacingRight
        {
            get => _facingRight;
            private set
            {
                if (_facingRight == value) return;
                _facingRight = value;
                if (_sr != null) _sr.flipX = !value;
            }
        }

        private int _jumpsLeft;
        private Collider2D _col;
        private float _coyoteTimer;

        // ===== TEMP-DEMO 自动试玩（全链路验收用，验证完删除）=====
        [SerializeField] public bool _demo = true; // 默认开：诊断构建自动从头玩到尾
        private float _demoMove;
        private float _demoClock;
        private float _demoStepT;
        private int _demoStep;
        private bool _demoJumpQueued;
        private bool _demoAttackQueued;
        private bool _demoDashQueued;
        private float _demoLogT;
        private float _demoAttackTimer;
        private bool _demoHoldJump;      // TEMP-DEMO：模拟按住空格（可变跳跃高度不被砍）
        private float _demoHoldT;        // TEMP-DEMO：按住时长
        private float _demoLastX;        // TEMP-DEMO：卡墙检测
        private float _demoWallT;        // TEMP-DEMO：卡墙计时
        private float _demoWallHop;      // TEMP-DEMO：卡墙补跳倒计时
        private WeaponSelectPanel _demoWeaponPanel;
        private LevelUpPanel _demoLevelPanel;

        /// <summary>脚本化试玩大脑：选武器→走→跳→连击→冲刺→自动选卡过关，全程日志渲染帧与状态。</summary>
        private void DemoTick()
        {
            _demoClock += Time.unscaledDeltaTime;
            _demoLogT -= Time.unscaledDeltaTime;

            // UI 状态自动推进（鼠标/键盘都不用碰）
            var state = GameManager.Instance.State;
            if (state == GameState.WeaponSelect)
            {
                if (_demoWeaponPanel == null) _demoWeaponPanel = FindFirstObjectByType<WeaponSelectPanel>();
                if (_demoWeaponPanel != null && _demoClock > 1.5f)
                {
                    Debug.Log("[Demo] 自动选择长剑");
                    _demoWeaponPanel.Select(0);
                }
                return;
            }
            if (state == GameState.LevelUp)
            {
                if (_demoLevelPanel == null) _demoLevelPanel = FindFirstObjectByType<LevelUpPanel>();
                if (_demoLevelPanel != null && _demoClock > 0.8f)
                {
                    Debug.Log("[Demo] 自动升级：选爽点0 + 继续");
                    _demoLevelPanel.OnBoonPicked(0);
                    _demoLevelPanel.OnContinueClicked();
                    _demoLevelPanel = null;
                }
                return;
            }
            if (state == GameState.LevelClear)
            {
                Debug.Log("[Demo] 过关，自动进下一关");
                GameManager.Instance.ContinueToNextLevel();
                return;
            }
            if (state != GameState.Playing) return;

            // 战斗脚本：走2.5s→跳→二段跳→走1.5s→三连击→走1s→冲刺，循环
            _demoStepT -= Time.deltaTime;
            if (_demoStepT <= 0f)
            {
                _demoStep = (_demoStep + 1) % 7;
                _demoStepT = _demoStep switch
                {
                    0 => 2.5f,  // 走
                    1 => 0.1f,  // 起跳
                    2 => 0.45f, // 二段跳
                    3 => 1.5f,  // 走（落地）
                    4 => 1.3f,  // 三连击窗口
                    5 => 1.0f,  // 走
                    _ => 2.0f,  // 冲刺+走
                };
                switch (_demoStep)
                {
                    case 1: _demoJumpQueued = true; Debug.Log("[Demo] 跳"); break;
                    case 2: _demoJumpQueued = true; Debug.Log("[Demo] 二段跳"); break;
                    case 4: Debug.Log("[Demo] 三连击开始"); break;
                    case 6: _demoDashQueued = true; Debug.Log("[Demo] 冲刺"); break;
                }
            }
            _demoMove = 1f; // 全程向右推进关卡
            if (_demoStep == 4)
            {
                _demoAttackTimer -= Time.deltaTime;
                if (_demoAttackTimer <= 0f) { _demoAttackTimer = 0.38f; _demoAttackQueued = true; }
            }
            if (_demoJumpQueued) { _jumpBufferTimer = jumpBufferTime; _demoJumpQueued = false; _demoHoldT = 0.55f; }

            // 按住跳模拟：起跳后 0.55s 内视为按住（可变跳跃高度不吃低跳惩罚）
            if (_demoHoldT > 0f) { _demoHoldT -= Time.deltaTime; _demoHoldJump = true; }
            else _demoHoldJump = false;

            // 卡墙自愈：1.5s 位移 < 0.5u → 立即跳+快速二段跳（模拟玩家"撞墙就跳"）
            if (Mathf.Abs(transform.position.x - _demoLastX) > 0.5f)
            {
                _demoLastX = transform.position.x;
                _demoWallT = 0f;
            }
            else if (IsGrounded())
            {
                _demoWallT += Time.deltaTime;
                if (_demoWallT > 1.5f)
                {
                    _demoWallT = 0f;
                    _jumpBufferTimer = jumpBufferTime;
                    _demoHoldT = 0.55f;
                    _demoWallHop = 0.35f; // 0.35s 后补一次二段跳
                    Debug.Log("[Demo] 卡墙，自动跳+二段跳");
                }
            }
            if (_demoWallHop > 0f)
            {
                _demoWallHop -= Time.deltaTime;
                if (_demoWallHop <= 0f && !IsGrounded()) { _jumpBufferTimer = jumpBufferTime; _demoHoldT = 0.5f; }
            }

            // 高频日志：抓"过一段时间变贴纸"的现场
            if (_demoLogT <= 0f)
            {
                _demoLogT = 0.25f;
                Debug.Log("[Demo] t=" + _demoClock.ToString("F1")
                    + " sprite=" + (_sr.sprite != null ? _sr.sprite.name : "null")
                    + " atkTimer=" + _attackTimer.ToString("F2")
                    + " combo=" + _comboStage
                    + " grounded=" + IsGrounded()
                    + " tScale=" + Time.timeScale.ToString("F2")
                    + " facing=" + (_facingRight ? "R" : "L")
                    + " hp=" + CurrentHp.ToString("F0")
                    + " x=" + transform.position.x.ToString("F0")
                    + " y=" + transform.position.y.ToString("F1"));
            }
        }
        // ===== TEMP-DEMO END =====
        private float _jumpBufferTimer;
        private float _invulnTimer;
        private float _dashTimer;
        private float _dashCdTimer;
        private float _attackTimer;
        private float _comboWindowTimer;   // 连击链保持窗口
        private int _comboStage;           // 0=未在连击中，1/2/3
        private float _bowChargeTimer;
        private bool _wasGrounded = true;  // 落地检测：上一帧的接地状态（空中→接地翻转瞬间触发压扁/尘土）
        private ProceduralAnim _pa;        // 程序化动画（跳跃拉伸/落地压扁/攻击 punch）
        private DustPuff _dust;            // 灰尘（起跳/落地/奔跑/冲刺）
        private static readonly Color NormalColor = Color.white;

        // 连击判定参数（调研惯例：段间递增明显，finisher 最痛）
        private static readonly float[] ComboDamageMul = { 1f, 1.4f, 1.8f };

        private void Reset()
        {
            _rb = GetComponent<Rigidbody2D>();
            _sr = GetComponentInChildren<SpriteRenderer>();
        }

        private void Awake()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 3.4f;          // 比真实重力更"游戏感"的下坠
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _pa = GetComponent<ProceduralAnim>();
            _dust = GetComponent<DustPuff>();
            CurrentHp = Stats.MaxHp;
            CurrentMana = Stats.MaxMana;
        }

        private void Start()
        {
            GameEvents.PublishPlayerHpChanged(CurrentHp, Stats.MaxHp);
            GameEvents.PublishPlayerManaChanged(CurrentMana, Stats.MaxMana);
            GameEvents.PublishXpChanged(0, XpToNext);
        }

        // ---------- 经验/升级（复用 GameManager 升级暂停队列） ----------

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int XpToNext => 10 + Level * 8 + Level * Level;

        public void AddXp(int amount)
        {
            if (IsDead) return;
            Xp += amount;
            int ups = 0;
            while (Xp >= XpToNext) { Xp -= XpToNext; Level++; ups++; }
            GameEvents.PublishXpChanged(Xp, XpToNext);
            if (ups > 0)
            {
                Stats.pendingTalentPoints += ups; // 每级 +1 天赋点
                GameEvents.PublishTalentPointsChanged(Stats.pendingTalentPoints);
                GameEvents.PublishLevelUp(Level);
                GameManager.Instance.RequestLevelUps(ups);
            }
        }

        // ---------- 伤害 ----------

        public void TakeDamage(in DamageInfo damage)
        {
            if (IsDead || _invulnTimer > 0f || _dashTimer > 0f) return;
            if (GameManager.Instance.State != GameState.Playing) return;

            CurrentHp -= damage.Amount;
            _invulnTimer = invulnerableDuration;
            GameEvents.PublishPlayerHpChanged(CurrentHp, Stats.MaxHp);
            GameEvents.PublishPlayerHurt(damage.SourcePosition);

            Juice.HitStop(0.05f);
            CameraRig.Instance?.AddTrauma(0.3f);
            AudioService.Instance?.Play(SfxId.PlayerHurt);

            // 受击小击退（背向伤害来源）
            _rb.velocity = new Vector2(
                Mathf.Sign(transform.position.x - damage.SourcePosition.x) * 3.5f, _rb.velocity.y);

            if (CurrentHp <= 0f)
            {
                CurrentHp = 0f;
                GameEvents.PublishPlayerHpChanged(0f, Stats.MaxHp);
                GameManager.Instance.HandlePlayerDeath();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            CurrentHp = Mathf.Min(Stats.MaxHp, CurrentHp + amount);
            GameEvents.PublishPlayerHpChanged(CurrentHp, Stats.MaxHp);
        }

        // ---------- 主循环 ----------

        private void Update()
        {
            if (_demo) DemoTick(); // TEMP-DEMO（在 Playing 门外运行：选武器/升级/结算状态也要自动推进）
            if (GameManager.Instance.State != GameState.Playing) return;
            var kb = Keyboard.current;

            DriveFrameAnimation();

            // 无敌帧闪烁
            if (_invulnTimer > 0f)
            {
                _invulnTimer -= Time.deltaTime;
                bool blink = Mathf.PingPong(Time.time * 12f, 1f) > 0.5f;
                _sr.color = blink ? hurtColor : NormalColor;
                if (_invulnTimer <= 0f) _sr.color = NormalColor;
            }

            _dashCdTimer -= Time.deltaTime;
            _attackTimer -= Time.deltaTime;
            _comboWindowTimer -= Time.deltaTime;
            if (_comboWindowTimer <= 0f) _comboStage = 0;

            // 蓝量回复
            float manaMax = Stats.MaxMana;
            if (CurrentMana < manaMax)
            {
                CurrentMana = Mathf.Min(manaMax, CurrentMana + Stats.manaRegen * Time.deltaTime);
                GameEvents.PublishPlayerManaChanged(CurrentMana, manaMax);
            }

            // 动画状态：Idle/Run（Moving）+ Jump/Fall（Airborne+Falling），五态真帧动画，状态机见构建器
            if (_animator != null)
            {
                bool moving = IsGrounded() && Mathf.Abs(_rb.velocity.x) > 0.3f;
                if (_animator.GetBool("Moving") != moving)
                    _animator.SetBool("Moving", moving);
                bool airborne = !IsGrounded();
                if (_animator.GetBool("Airborne") != airborne)
                    _animator.SetBool("Airborne", airborne);
                // 上升/下落分开驱动：起跳收腿帧与下落展腿帧切换（帧图来自用户 AI 生图批次二）
                bool falling = airborne && _rb.velocity.y < -0.5f;
                if (_animator.GetBool("Falling") != falling)
                    _animator.SetBool("Falling", falling);
            }

            // 灰尘：接地移动时跑动扬尘（DustPuff 内部 0.15s 节流）
            if (_dust != null)
                _dust.SetRunning(IsGrounded() && Mathf.Abs(_rb.velocity.x) > 0.5f);

            // 跳跃缓冲 + 土狼时间（平台手感三件套之二）
            _jumpBufferTimer -= Time.deltaTime;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) _jumpBufferTimer = jumpBufferTime;

            // 冲刺（L / 手柄 B）
            if (DashReady && kb != null && kb.lKey.wasPressedThisFrame)
                TryDash();

            // 攻击（J）：按武器分派（TEMP-DEMO：自动试玩接管攻击/冲刺输入）
            if (_demo && _demoAttackQueued) { _demoAttackQueued = false; TrySwordSwing(); }
            else if (_demo) { if (_demoDashQueued) { _demoDashQueued = false; TryDash(); } }
            else ReadWeaponInput(kb);

            // 特殊技（K）
            if (kb != null && kb.kKey.wasPressedThisFrame) TrySpecial();
        }

        private void FixedUpdate()
        {
            if (GameManager.Instance.State != GameState.Playing) return;

            // 冲刺：覆盖常规移动
            if (_dashTimer > 0f)
            {
                _dashTimer -= Time.fixedDeltaTime;
                _rb.velocity = new Vector2((FacingRight ? 1f : -1f) * dashSpeed, 0f);
                SpawnGhost();
                return;
            }

            var kb = Keyboard.current;
            float move = 0f;
            if (_demo) move = _demoMove; // TEMP-DEMO：自动试玩接管移动（验证完删除）
            else
            {
                if (kb != null)
                {
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += 1f;
                }
                var pad = Gamepad.current;
                if (pad != null && Mathf.Abs(pad.leftStick.x.value) > 0.15f) move = pad.leftStick.x.value;
            }

            if (move != 0f) FacingRight = move > 0f;
            _rb.velocity = new Vector2(move * Stats.MoveSpeed, _rb.velocity.y);

            // 跳跃执行：缓冲命中且（在地面或土狼时间内或有二段跳）
            if (_jumpBufferTimer > 0f)
            {
                bool grounded = IsGrounded();
                if (grounded) _coyoteTimer = coyoteTime;
                else _coyoteTimer -= Time.fixedDeltaTime;

                if (_coyoteTimer > 0f)
                {
                    _rb.velocity = new Vector2(_rb.velocity.x, jumpVelocity);
                    _jumpsLeft = 1; // 剩一次空中跳
                    _jumpBufferTimer = 0f;
                    _coyoteTimer = 0f;
                    _pa?.NotifyJump();  // 起跳拉伸
                    _dust?.EmitJump();  // 起跳扬尘
                }
                else if (_jumpsLeft > 0)
                {
                    _rb.velocity = new Vector2(_rb.velocity.x, jumpVelocity * 0.92f);
                    _jumpsLeft--;
                    _jumpBufferTimer = 0f;
                    _pa?.NotifyJump();  // 二段跳同样拉伸反馈
                    _dust?.EmitJump();
                }
            }

            // 落地复位二段跳
            bool groundedNow = IsGrounded();
            if (groundedNow && _rb.velocity.y <= 0.1f) _jumpsLeft = 1;

            // 落地瞬间（空中→接地翻转）：压扁 + 尘土，"重量感"的核心反馈
            if (groundedNow && !_wasGrounded && _rb.velocity.y <= 0.1f)
            {
                _pa?.NotifyLand();
                _dust?.EmitLand();
            }
            _wasGrounded = groundedNow;

            // 可变跳跃高度 + apex 悬停（只在空中生效；冲刺激活时 y≈0 会被 apex 误触，无碍）
            if (!IsGrounded())
            {
                bool spaceHeld = _demo ? _demoHoldJump : (kb != null && kb.spaceKey.isPressed); // TEMP-DEMO：模拟按住空格
                if (_rb.velocity.y < -0.1f)
                    _rb.velocity += Vector2.up * (Physics2D.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime);
                else if (_rb.velocity.y > 1f && !spaceHeld)
                    _rb.velocity += Vector2.up * (Physics2D.gravity.y * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime);
                else if (Mathf.Abs(_rb.velocity.y) < 1f)
                    _rb.velocity += Vector2.up * (Physics2D.gravity.y * (apexMultiplier - 1f) * Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// 代码驱动换帧（真帧动画的唯一执行者）：按 攻击动作 > 空中升降 > 跑步循环 > 站姿
        /// 的优先级选帧组写入 _sr。为何不用 Animator：自动试玩实证——程序化 PPtr 换帧曲线在
        /// Player 端虽生效，但攻击状态播完会卡在末帧持续覆盖（"过一会儿变贴纸"的元凶），
        /// 且与代码驱动形成双写竞争。预制体已不挂 Animator，朝向翻转由 FacingRight 写 _sr.flipX。
        /// </summary>
        private void DriveFrameAnimation()
        {
            if (_sr == null || _runFrames == null || _runFrames.Length == 0) return; // 帧未注入（旧预制体）→ 不干预

            // 攻击动作（0.3s 内按攻击进度播 蓄力→突刺→收招；法杖/弓箭锁定中无需分武器）
            float atkInterval = _weaponKit != null ? _weaponKit.attackInterval : 0.4f;
            if (_attackFrames != null && _attackFrames.Length > 0 && _attackTimer > atkInterval - 0.3f)
            {
                float p = Mathf.Clamp01((atkInterval - _attackTimer) / 0.3f);
                _sr.sprite = _attackFrames[Mathf.Min((int)(p * _attackFrames.Length), _attackFrames.Length - 1)];
                _runFrameIndex = 0;
                return;
            }

            if (!IsGrounded())
            {
                var air = Pick(_rb.velocity.y < -0.5f ? _fallFrames : _jumpFrames);
                if (air != null) _sr.sprite = air;
                _runFrameIndex = 0;
                return;
            }

            if (Mathf.Abs(_rb.velocity.x) > 0.3f)
            {
                // 跑步四拍循环：0.12s/帧（0→1→2→1→…），速度只影响节奏不影响帧序
                _runFrameTimer -= Time.deltaTime;
                if (_runFrameTimer <= 0f)
                {
                    _runFrameTimer = 0.12f;
                    _runFrameIndex = (_runFrameIndex + 1) % _runFrames.Length;
                }
                _sr.sprite = _runFrames[_runFrameIndex];
                return;
            }

            var idle = Pick(_idleFrames);
            if (idle != null) _sr.sprite = idle;
            _runFrameIndex = 0;
        }

        private static Sprite Pick(Sprite[] frames) =>
            frames != null && frames.Length > 0 ? frames[0] : null;

        private bool IsGrounded()
        {
            // 探测盒锚定碰撞体底缘（world bounds：尺寸/偏移/缩放调整自动跟随，不再写死 0.55u）
            var col = _col != null ? _col : (_col = GetComponent<Collider2D>());
            float bottom = col != null ? col.bounds.center.y - col.bounds.extents.y : transform.position.y - 0.1f;
            var origin = new Vector2(transform.position.x, bottom - 0.02f);
            // 无视 Trigger：教学牌/传送门挂在 Ground(8) 层的触发盒——空中穿过教学牌光柱会被误判
            // "接地"，二段跳计数被空中重置 = 无限跳（用户实测复现的 bug 根因）。
            // Enemy(7) 层的实体碰撞体也算"可站立"：踩在怪头上不算悬空（否则跳不起步、动画锁空中帧）
            foreach (var hit in Physics2D.OverlapBoxAll(origin, new Vector2(0.5f, 0.24f), 0f, groundMask | (1 << 7)))
                if (!hit.isTrigger) return true;
            return false;
        }

        private void TryDash()
        {
            _dashCdTimer = Boons.Has(BoonIds.UniversalDashMaster) ? dashCooldown * 0.6f : dashCooldown;
            _dashTimer = dashDuration;
            _invulnTimer = Mathf.Max(_invulnTimer, dashInvuln);
            _dust?.DashTrail(dashDuration); // 冲刺沿途尾尘（每 0.03s 一粒）
            BoonEffects.OnDash(this); // 火焰冲刺等爽点钩子
            AudioService.Instance?.PlayWithPitch(SfxId.Shoot, 0.6f);
        }

        private void SpawnGhost()
        {
            var ghost = new GameObject("HeroGhost");
            ghost.transform.position = transform.position;
            // 残影继承当前视觉姿态（含程序化动画的压扁/拉伸），冲刺更像"从身体里抽出来的影子"
            ghost.transform.localScale = _sr != null ? _sr.transform.lossyScale : transform.localScale;
            var gsr = ghost.AddComponent<SpriteRenderer>();
            gsr.sprite = _sr.sprite;
            gsr.flipX = _sr.flipX;
            gsr.color = new Color(0.5f, 0.85f, 1f, 0.45f);
            gsr.sortingOrder = _sr.sortingOrder - 1;
            var fb = ghost.AddComponent<FadeOutAndDie>();
            fb.life = 0.22f;
        }

        // ---------- 攻击（按武器分派） ----------

        private void ReadWeaponInput(Keyboard kb)
        {
            if (kb == null) return;
            if (kb.jKey.wasPressedThisFrame) PrimaryPressed();
            // 弓箭蓄力：按住 J 计时；松开即清零（防"蓄力残留"让下次点射直接变狙击）
            if (Kind == WeaponKind.Bow)
                _bowChargeTimer = kb.jKey.isPressed ? _bowChargeTimer + Time.deltaTime : 0f;
        }

        private void PrimaryPressed()
        {
            switch (Kind)
            {
                case WeaponKind.Sword: TrySwordSwing(); break;
                case WeaponKind.Staff: TryFireball(); break;
                case WeaponKind.Bow: TryBowShot(charged: _bowChargeTimer >= 0.5f && Boons.Has(BoonIds.BowPowerShot)); break;
            }
        }

        private void TrySwordSwing()
        {
            if (_attackTimer > 0f) return;
            _attackTimer = Stats.AttackInterval;
            if (_comboWindowTimer > 0f) _comboStage = Mathf.Min(3, _comboStage + 1);
            else _comboStage = 1;
            _comboWindowTimer = 0.9f;

            // 攻击要"带身体"：接地时向前小冲量（惯性滑步），挥砍有踩进去的力道
            if (IsGrounded())
            {
                _rb.velocity += new Vector2((FacingRight ? 1f : -1f) * 2.5f, 0f);
                _dust?.Emit(1); // 蹬地滑步扬起一粒尘
            }
            _pa?.NotifyAttack(); // 攻击 punch（横向扩+纵向收的弹性反馈）

            float dmg = Stats.Attack * ComboDamageMul[_comboStage - 1];
            float reach = 1.35f * (Boons.Has(BoonIds.SwordLong) ? 1.4f : 1f);
            var box = new Vector2(reach, 1.3f);
            var center = (Vector2)_attackOrigin.position + Vector2.right * (FacingRight ? reach * 0.4f : -reach * 0.4f);
            int mask = 1 << 7; // Enemy 层

            bool hitAny = false;
            foreach (var hit in Physics2D.OverlapBoxAll(center, box, 0f, mask))
            {
                var enemy = hit.GetComponentInParent<EnemyBase>();
                if (enemy == null || enemy.IsDead) continue;
                enemy.TakeDamage(new DamageInfo(dmg, transform.position, false));
                BoonEffects.OnMeleeHit(this, enemy);
                hitAny = true;
            }

            // 弧光：尺寸 = 等级成长 × SwordLong 强化（1.4 倍），颜色/角度/加色/层级都在 Juice 内处理
            float arcScale = (1f + Level * 0.05f) * (Boons.Has(BoonIds.SwordLong) ? 1.4f : 1f);
            Juice.SlashArc(_attackOrigin.position, FacingRight, _comboStage, arcScale);
            _animator?.SetTrigger("Attack");
            AudioService.Instance?.PlayWithPitch(SfxId.Hit, 0.9f + _comboStage * 0.08f);
            if (_comboStage == 3)
            {
                CameraRig.Instance?.AddTrauma(0.3f); // 收尾段升档震屏（调研配额：重击 0.3-0.4）
                BoonEffects.OnComboFinisher(this, center);
                if (Boons.Has(BoonIds.SwordShockwave)) FireSwordWave();
            }
            if (hitAny) Juice.HitStop(_comboStage == 3 ? 0.06f : 0.03f); // 近战命中停帧确认有效（0.15s 冷却节流）
        }

        private void FireSwordWave()
        {
            var wave = ProjectilePool.Spawn(ProjectilePool.SwordWave, transform.position + Vector3.up * 0.4f, Quaternion.identity);
            wave.GetComponent<Projectile>().Launch(new Vector2(FacingRight ? 1f : -1f, 0f), 9f, Stats.Attack * 1.6f, false, 3, 1.2f);
        }

        private void TryFireball()
        {
            if (_attackTimer > 0f) return;
            // 设计修正（用户反馈）：普攻免费——蓝只作为"K 特殊技"的资源，
            // 否则没蓝时法杖会彻底哑火（普攻不能被资源锁死）
            _attackTimer = _weaponKit != null ? _weaponKit.attackInterval : 0.4f;
            _pa?.NotifyAttack(); // 施法 punch：轻一顿的后坐反馈
            LaunchFireball(1f);
            if (Boons.Has(BoonIds.StaffTwinBolt)) LaunchFireball(0.75f);
            AudioService.Instance?.PlayWithPitch(SfxId.Shoot, 1.25f);
        }

        private void LaunchFireball(float mul)
        {
            var go = ProjectilePool.Spawn(ProjectilePool.Fireball, _attackOrigin.position, Quaternion.identity);
            var fb = go.GetComponent<Fireball>();
            fb.Launch(new Vector2(FacingRight ? 1f : -1f, 0f), Stats.Attack * 1.4f * mul,
                Boons.Has(BoonIds.StaffBigBurst) ? 1.6f : 1f, Boons.Has(BoonIds.StaffFireballSplit));
        }

        private void TryBowShot(bool charged)
        {
            if (_attackTimer > 0f) return;
            _attackTimer = _weaponKit != null ? _weaponKit.attackInterval : 0.45f;
            _bowChargeTimer = 0f;
            _pa?.NotifyAttack(); // 开弓 punch：撒放瞬间的身体反馈

            float dmg = Stats.Attack * (charged ? 2.5f : 1f) * (_weaponKit != null ? _weaponKit.attackDamageMul : 1f);
            int pierce = 1 + (Boons.Has(BoonIds.BowPiercePlus) ? 1 : 0);
            int arrows = Boons.Has(BoonIds.BowMultishot) ? 2 : 1;
            for (int i = 0; i < arrows; i++)
            {
                var go = ProjectilePool.Spawn(ProjectilePool.Arrow, _attackOrigin.position, Quaternion.identity);
                float ang = arrows > 1 ? (i == 0 ? 4f : -4f) : 0f;
                var dir = Quaternion.Euler(0, 0, ang) * new Vector3(FacingRight ? 1f : -1f, 0f, 0f);
                go.GetComponent<Projectile>().Launch(dir, charged ? 16f : 13f, dmg, charged, pierce, 1.6f);
            }
            if (Boons.Has(BoonIds.BowBackstep))
                _rb.velocity = new Vector2((FacingRight ? -1f : 1f) * 4.5f, _rb.velocity.y + 1.5f);
            AudioService.Instance?.PlayWithPitch(SfxId.Shoot, 1.45f);
        }

        private void TrySpecial()
        {
            switch (Kind)
            {
                case WeaponKind.Sword:
                    // 剑气是爽点解锁项（TrySwordSwing 里触发），K 留给震地斩
                    if (Boons.Has(BoonIds.SwordQuakeSlam) && !IsGrounded()) BoonEffects.OnQuakeSlam(this);
                    break;
                case WeaponKind.Staff:
                    TryStaffNova();
                    break;
                case WeaponKind.Bow:
                    if (Boons.Has(BoonIds.BowArrowRain)) BoonEffects.OnArrowRain(this);
                    break;
            }
        }

        private void TryStaffNova()
        {
            if (CurrentMana < fireballManaCost * 2f) return;
            CurrentMana -= fireballManaCost * 2f;
            GameEvents.PublishPlayerManaChanged(CurrentMana, Stats.MaxMana);
            BoonEffects.OnStaffNova(this); // 冰霜新星/普通新星都走这里（冻结与否看爽点）
            CameraRig.Instance?.AddTrauma(0.25f);
        }
    }
}
