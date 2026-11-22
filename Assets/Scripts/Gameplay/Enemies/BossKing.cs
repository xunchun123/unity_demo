using StarVortex.Core;
using StarVortex.Gameplay;
using UnityEngine;

namespace StarVortex.Enemies
{
    /// <summary>
    /// BOSS·深渊魔王：三阶段状态机（600 HP / 100 经验由关卡引导器注入）。
    /// 阶段 1（HP > 60%）：剑气三连挥砍（前摇 0.5s 红光闪烁 3 次，判定盒 2×2，单次 15 血，冷却 2s），缓慢追击 2.2；
    /// 阶段 2（60% ≥ HP > 30%）：新增地面尖刺阵（前摇 0.8s，向勇者方向依序冒出 6 组尖刺，碰触扣 14），保留挥砍；
    /// 阶段 3（HP ≤ 30%）：狂暴——速度 3.5、前摇缩短 30%、召唤 2 只史莱姆 + 环形 12 发弹幕；
    /// 阶段切换瞬间：吼叫震屏 + 1s 硬无敌 + 广播关卡阶段事件。
    /// 面试点：出招前摇统一"变色闪烁 + 停顿"，BOSS 的恐怖感不来自数值碾压，而来自
    /// "招式全部可读、躲开就有回报"的多阶段对抗节奏——每次转阶段都迫使玩家更新对策。
    /// </summary>
    public class BossKing : EnemyBase
    {
        [Header("追击")]
        [SerializeField] private float chaseSpeed = 2.2f;         // 阶段 1/2 追击速度
        [SerializeField] private float enragedSpeed = 3.5f;       // 阶段 3 狂暴速度
        [SerializeField] private float enragedTelegraphMul = 0.7f; // 阶段 3 前摇缩短 30%

        [Header("技能：剑气三连")]
        [SerializeField] private float comboWindup = 1.0f;        // 前摇（红光闪烁 3 次）——名作甜点区 1.0-1.5s
        [SerializeField] private float comboSwingGap = 0.3f;      // 挥砍间隔
        [SerializeField] private float comboDamage = 15f;         // 单次挥砍伤害
        [SerializeField] private float comboCooldown = 2f;        // 冷却
        [SerializeField] private Vector2 comboBoxSize = new Vector2(2f, 2f); // 判定盒

        [Header("技能：地面尖刺阵（阶段 2+）")]
        [SerializeField] private float spikeWindup = 1.2f;        // 前摇——全屏类招式给到甜点区上限附近
        [SerializeField] private int spikeCount = 6;              // 尖刺组数
        [SerializeField] private float spikeGap = 0.15f;          // 依序冒出的间隔
        [SerializeField] private float spikeSpacing = 1.2f;       // 组间距
        [SerializeField] private float spikeLife = 0.5f;          // 单组刺盒存活时长
        [SerializeField] private float spikeDamage = 14f;         // 尖刺伤害
        [SerializeField] private float spikeCooldown = 4.5f;      // 冷却（demo 平衡值，可调）

        [Header("技能：召唤（阶段 3）")]
        [SerializeField] private Slime slimePrefab;               // 史莱姆 prefab（关卡引导器注入，null 时跳过召唤）
        [SerializeField] private float summonWindup = 0.5f;
        [SerializeField] private float summonCooldown = 6f;

        [Header("技能：环形弹幕（阶段 3）")]
        [SerializeField] private float novaWindup = 0.5f;
        [SerializeField] private int novaCount = 12;              // 环形弹数
        [SerializeField] private float novaBoltSpeed = 7f;        // 弹幕速度（与法师火球同速）
        [SerializeField] private float novaBoltDamage = 12f;      // 弹幕伤害
        [SerializeField] private float novaCooldown = 5f;

        private enum Phase { One = 1, Two = 2, Three = 3 }
        private enum State { Chase, Windup, Combo, Spiking }
        private enum Skill { Combo, Spikes, Summon, Nova }

        private static readonly Skill[] P1Skills = { Skill.Combo };
        private static readonly Skill[] P2Skills = { Skill.Combo, Skill.Spikes };
        private static readonly Skill[] P3Skills = { Skill.Combo, Skill.Spikes, Skill.Summon, Skill.Nova };

        private Phase _phase = Phase.One;
        private State _state = State.Chase;
        private int _skillIdx;                    // 技能轮换指针（阶段技能表内循环）
        private Skill _currentSkill;
        private float _windupTimer;
        private float _windupTotal;
        private int _comboStep;                   // 0..2 已挥砍次数
        private int _spikeStep;                   // 0..5 已冒出组数
        private float _stepTimer;
        private float _spikeDir = 1f;             // 尖刺阵方向（出手瞬间锁定）
        private float _comboCd;
        private float _spikeCd;
        private float _summonCd;
        private float _novaCd;
        private float _phaseInvulnTimer;          // 阶段切换 1s 硬无敌
        private float _frozenTimer;               // 冻结契约（SetFrozen）：>0 时跳过行为、velocity 清零

        private static readonly Color WindupRed = new Color(1f, 0.3f, 0.22f);
        private static readonly Color WindupPurple = new Color(0.85f, 0.3f, 0.95f);
        private static readonly Vector3 BaseScale = Vector3.one;

        public override void Init(Transform hero)
        {
            base.Init(hero);
            _phase = Phase.One;
            _state = State.Chase;
            _skillIdx = 0;
            _windupTimer = 0f;
            _comboStep = 0;
            _spikeStep = 0;
            _stepTimer = 0f;
            // 出场 1s 技能缓冲：给玩家看清 BOSS 再挨第一刀
            _comboCd = 1f; _spikeCd = 1f; _summonCd = 1f; _novaCd = 1f;
            _phaseInvulnTimer = 0f;
            _frozenTimer = 0f;
            _contactTimer = 0f;
            transform.localScale = BaseScale;
            if (_sr != null) { _sr.color = Color.white; _sr.flipX = false; }
            if (_rb != null) _rb.velocity = Vector2.zero;
            GameEvents.PublishBossHpChanged(_hp, maxHp); // 出场同步血条
        }

        /// <summary>冻结：打断出招（前摇/连段全部作废回追击），颜色姿态复位。</summary>
        public override void SetFrozen(float seconds)
        {
            _frozenTimer = Mathf.Max(_frozenTimer, seconds);
            if (_rb != null) _rb.velocity = Vector2.zero;
            transform.localScale = BaseScale;
            if (_sr != null) _sr.color = Color.white;
            _state = State.Chase;
        }

        protected override void Update()
        {
            if (_frozenTimer > 0f)
            {
                _frozenTimer -= Time.deltaTime;
                if (_rb != null) _rb.velocity = Vector2.zero;
                return;
            }
            base.Update();
        }

        // ---------- 受击 / 阶段 ----------

        public override void TakeDamage(in DamageInfo damage)
        {
            if (IsDead) return;
            if (_phaseInvulnTimer > 0f) return; // 转阶段吼叫后的 1s 喘息，硬无敌
            base.TakeDamage(damage);
            if (!IsDead) GameEvents.PublishBossHpChanged(_hp, maxHp); // 每次受击都推血条
        }

        protected override void OnDeath()
        {
            GameEvents.PublishBossHpChanged(0f, 0f); // 死亡契约：血条归零（0, 0）
            if (PoolService.Instance != null) PoolService.Instance.Despawn(gameObject);
            else gameObject.SetActive(false);
        }

        /// <summary>按血量比例结算当前阶段（>60% 一阶 / >30% 二阶 / 其余三阶），越阶伤害直接跳阶。</summary>
        private void CheckPhase()
        {
            float ratio = Mathf.Clamp01(_hp / Mathf.Max(1f, maxHp));
            var phase = ratio > 0.6f ? Phase.One : ratio > 0.3f ? Phase.Two : Phase.Three;
            if (phase != _phase) EnterPhase(phase);
        }

        /// <summary>阶段切换：吼叫（震屏+音效）→ 1s 硬无敌 → 广播阶段事件 → 打断当前招式回追击。</summary>
        private void EnterPhase(Phase phase)
        {
            _phase = phase;
            _phaseInvulnTimer = 1f;
            // 吼叫：CameraRig 内部对单次 trauma 有 0.3 钳制，按契约传 0.5 即可
            CameraRig.Instance?.AddTrauma(0.5f);
            AudioService.Instance?.Play(SfxId.EliteKill); // 暂用精英击杀音色充当吼叫
            Juice.Flash(_sr, Color.white, 0.15f);
            GameEvents.PublishLevelPhaseChanged(-1, $"BOSS 阶段 {(int)phase}");

            // 打断出招回追击（前摇中的招作废），姿态复位
            _state = State.Chase;
            _windupTimer = 0f;
            transform.localScale = BaseScale;
            if (_sr != null) _sr.color = Color.white;
        }

        // ---------- 行为主体 ----------

        protected override void Behaviour()
        {
            if (_rb == null) return;
            if (_sr != null) _sr.flipX = !_facingRight;

            _phaseInvulnTimer -= Time.deltaTime;
            _comboCd -= Time.deltaTime;
            _spikeCd -= Time.deltaTime;
            _summonCd -= Time.deltaTime;
            _novaCd -= Time.deltaTime;
            CheckPhase();

            switch (_state)
            {
                case State.Chase:
                {
                    // 缓慢追击（阶段 3 狂暴提速）；有技能转好就停下出招
                    float speed = _phase == Phase.Three ? enragedSpeed : chaseSpeed;
                    float dx = _hero.position.x - transform.position.x;
                    if (Mathf.Abs(dx) > 0.8f)
                        _rb.velocity = new Vector2(Mathf.Sign(dx) * speed, _rb.velocity.y);
                    else
                        _rb.velocity = new Vector2(0f, _rb.velocity.y);

                    var ready = PickReadySkill();
                    if (ready.HasValue) StartWindup(ready.Value);
                    break;
                }

                case State.Windup:
                {
                    // 统一前摇：变色闪烁 + 停顿（玩家唯一的反应窗口）
                    _rb.velocity = new Vector2(0f, _rb.velocity.y);
                    _windupTimer -= Time.deltaTime;
                    TelegraphTick();
                    if (_windupTimer <= 0f)
                    {
                        if (_sr != null) _sr.color = Color.white;
                        ExecuteSkill(_currentSkill);
                    }
                    break;
                }

                case State.Combo:
                {
                    // 三连挥砍：判定盒 2×2 在面向侧，间隔 0.3s，站定连砍
                    _rb.velocity = new Vector2(0f, _rb.velocity.y);
                    _stepTimer -= Time.deltaTime;
                    if (_stepTimer <= 0f && _comboStep < 3)
                    {
                        DoSwing(_comboStep);
                        _comboStep++;
                        _stepTimer = comboSwingGap;
                        if (_comboStep >= 3)
                        {
                            _comboCd = comboCooldown;
                            _state = State.Chase;
                        }
                    }
                    break;
                }

                case State.Spiking:
                {
                    // 尖刺阵：从自身向勇者方向依序冒出，间隔 0.15s，站定施法
                    _rb.velocity = new Vector2(0f, _rb.velocity.y);
                    _stepTimer -= Time.deltaTime;
                    if (_stepTimer <= 0f && _spikeStep < spikeCount)
                    {
                        SpawnSpike(_spikeStep);
                        _spikeStep++;
                        _stepTimer = spikeGap;
                        if (_spikeStep >= spikeCount)
                        {
                            _spikeCd = spikeCooldown;
                            _state = State.Chase;
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>按阶段技能表轮换取第一个转好冷却的技能（召唤技能在无 prefab 时跳过）。</summary>
        private Skill? PickReadySkill()
        {
            var table = _phase == Phase.One ? P1Skills
                      : _phase == Phase.Two ? P2Skills
                      : P3Skills;
            for (int i = 0; i < table.Length; i++)
            {
                var skill = table[(_skillIdx + i) % table.Length];
                if (SkillReady(skill))
                {
                    _skillIdx = (_skillIdx + i + 1) % table.Length;
                    return skill;
                }
            }
            return null;
        }

        private bool SkillReady(Skill skill)
        {
            switch (skill)
            {
                case Skill.Combo: return _comboCd <= 0f;
                case Skill.Spikes: return _spikeCd <= 0f && _phase != Phase.One;
                case Skill.Summon: return _summonCd <= 0f && _phase == Phase.Three && slimePrefab != null;
                case Skill.Nova: return _novaCd <= 0f && _phase == Phase.Three;
                default: return false;
            }
        }

        private void StartWindup(Skill skill)
        {
            _currentSkill = skill;
            float baseWindup = skill == Skill.Combo ? comboWindup
                             : skill == Skill.Spikes ? spikeWindup
                             : skill == Skill.Summon ? summonWindup
                             : novaWindup;
            _windupTotal = _phase == Phase.Three ? baseWindup * enragedTelegraphMul : baseWindup;
            _windupTimer = _windupTotal;
            _state = State.Windup;
            // 出招前摇起手挤压 telegraph（PunchScale 与前摇的变色闪烁叠加，可读性翻倍）
            Juice.PunchScale(transform, 0.18f, Mathf.Min(0.4f, _windupTotal));
        }

        /// <summary>前摇视觉：三连砍红光闪烁 3 次，其余渐变到技能主题色。</summary>
        private void TelegraphTick()
        {
            if (_sr == null) return;
            if (_currentSkill == Skill.Combo)
            {
                float blink = Mathf.PingPong((1f - _windupTimer / Mathf.Max(0.01f, _windupTotal)) * 3f, 1f);
                _sr.color = Color.Lerp(Color.white, WindupRed, blink);
            }
            else
            {
                var target = _currentSkill == Skill.Spikes ? WindupPurple : WindupRed;
                float k = Mathf.Clamp01(1f - _windupTimer / Mathf.Max(0.01f, _windupTotal));
                _sr.color = Color.Lerp(Color.white, target, k);
            }
        }

        private void ExecuteSkill(Skill skill)
        {
            switch (skill)
            {
                case Skill.Combo:
                    _comboStep = 0;
                    _stepTimer = 0f; // 第一刀立刻挥出
                    _state = State.Combo;
                    break;
                case Skill.Spikes:
                    // 方向以出手瞬间为准，依序向勇者侧推进
                    _spikeDir = Mathf.Abs(_hero.position.x - transform.position.x) > 0.1f
                        ? Mathf.Sign(_hero.position.x - transform.position.x)
                        : (_facingRight ? 1f : -1f);
                    _spikeStep = 0;
                    _stepTimer = 0f;
                    _state = State.Spiking;
                    AudioService.Instance?.PlayWithPitch(SfxId.CritHit, 0.5f);
                    CameraRig.Instance?.AddTrauma(0.1f);
                    break;
                case Skill.Summon:
                    DoSummon();
                    _summonCd = summonCooldown;
                    _state = State.Chase;
                    break;
                case Skill.Nova:
                    DoNova();
                    _novaCd = novaCooldown;
                    _state = State.Chase;
                    break;
            }
        }

        /// <summary>单次挥砍：2×2 判定盒扫面向侧 + 剑气弧光视觉（复用勇者同款表现）。</summary>
        private void DoSwing(int step)
        {
            var center = (Vector2)transform.position
                         + Vector2.up * 0.6f
                         + Vector2.right * (_facingRight ? 1.2f : -1.2f);
            foreach (var hit in Physics2D.OverlapBoxAll(center, comboBoxSize, 0f))
            {
                var hero = hit.GetComponentInParent<HeroController>();
                if (hero == null || hero.IsDead) continue;
                hero.TakeDamage(new DamageInfo(comboDamage, transform.position, false));
                Juice.HitSpark(hero.transform.position, WindupRed);
            }
            Juice.SlashArc(transform.position, _facingRight, step + 1);
            AudioService.Instance?.PlayWithPitch(SfxId.Hit, 0.7f + step * 0.08f);
        }

        /// <summary>单组尖刺：沿锁定方向排布的触发器刺盒，冒出 0.5s。</summary>
        private void SpawnSpike(int step)
        {
            var pos = (Vector2)transform.position
                      + Vector2.right * (_spikeDir * (1.5f + step * spikeSpacing));
            SpikeColumn.Spawn(pos, spikeDamage, spikeLife);
        }

        /// <summary>召唤 2 只史莱姆（prefab 由关卡引导器注入；null 时整段跳过，技能不进轮换）。</summary>
        private void DoSummon()
        {
            SpawnSlime(slimePrefab != null ? slimePrefab.transform : null);
            SpawnSlime(slimePrefab != null ? slimePrefab.transform : null);
            AudioService.Instance?.PlayWithPitch(SfxId.Shoot, 0.5f);
        }

        /// <summary>召唤入口（公开契约）：prefab 为 null 时跳过；落点在 BOSS 两侧随机取一。</summary>
        public void SpawnSlime(Transform prefab)
        {
            if (prefab == null || _hero == null) return;
            float side = Random.value < 0.5f ? -1f : 1f;
            var offset = new Vector3(side * Random.Range(1f, 1.6f), 0.5f, 0f);
            var go = Instantiate(prefab, transform.position + offset, Quaternion.identity);
            var slime = go.GetComponent<Slime>();
            if (slime != null) slime.Init(_hero); // 新史莱姆走同一套 Init 契约（含池化复位路径）
        }

        /// <summary>环形弹幕：以 BOSS 为中心均匀 12 向发射 MageBolt（速度/伤害与法师火球一致）。</summary>
        private void DoNova()
        {
            var center = (Vector2)transform.position + Vector2.up * 0.8f;
            for (int i = 0; i < novaCount; i++)
            {
                float ang = 360f * i / Mathf.Max(1, novaCount);
                var dir = Quaternion.Euler(0f, 0f, ang) * Vector2.right;
                MageBolt.Spawn(center, dir, novaBoltSpeed, novaBoltDamage);
            }
            AudioService.Instance?.PlayWithPitch(SfxId.CritHit, 0.55f);
            CameraRig.Instance?.AddTrauma(0.2f);
        }

        /// <summary>击退：直接叠加速度（BOSS 体型大但契约统一，后期可在此按阶段做韧性减免）。</summary>
        public override void ApplyKnockback(Vector2 dir, float force)
        {
            if (IsDead || _rb == null) return;
            _rb.velocity += dir * force;
        }
    }

    /// <summary>
    /// BOSS 尖刺列：地面冒出的一次性刺盒（触发器 + 冒出动画 + 0.5s 自毁）。
    /// 命中判定走每帧 OverlapBox 轮询（不依赖层矩阵配置），单柱只命中一次（勇者无敌帧兜底重复判定）。
    /// </summary>
    internal class SpikeColumn : MonoBehaviour
    {
        private float _damage;
        private float _lifetime;
        private float _age;
        private bool _hitHero;
        private static readonly Color SpikeColor = new Color(0.95f, 0.35f, 1f, 0.9f); // 尖刺紫

        /// <summary>生成一根尖刺（现场建 GameObject，短寿命特效不值得池化）。</summary>
        public static SpikeColumn Spawn(Vector2 pos, float damage, float life)
        {
            var go = new GameObject("BossSpike");
            go.transform.position = pos;
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0) go.layer = enemyLayer;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.9f, 1.3f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpritePool.GlowDisc; // 光晕盘纵向拉长，读起来就是地刺喷发
            sr.color = SpikeColor;
            sr.sortingOrder = 8;
            go.transform.localScale = new Vector3(0.8f, 0.15f, 1f); // 从地里冒出的初始姿态

            var spike = go.AddComponent<SpikeColumn>();
            spike._damage = damage;
            spike._lifetime = life;
            return spike;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            // 冒出动画：0.12s 内纵向长高到满
            float rise = Mathf.Clamp01(_age / 0.12f);
            transform.localScale = new Vector3(0.8f, Mathf.Lerp(0.15f, 1.2f, rise), 1f);

            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f) { Destroy(gameObject); return; }

            if (_hitHero) return;
            foreach (var hit in Physics2D.OverlapBoxAll(transform.position, new Vector2(0.9f, 1.3f), 0f))
            {
                var hero = hit.GetComponentInParent<HeroController>();
                if (hero == null || hero.IsDead) continue;
                _hitHero = true;
                hero.TakeDamage(new DamageInfo(_damage, transform.position, false));
                Juice.HitSpark(hero.transform.position, SpikeColor);
                return;
            }
        }
    }
}
