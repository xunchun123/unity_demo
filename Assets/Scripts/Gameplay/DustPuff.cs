using System.Collections.Generic;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 落地/起跳/奔跑/冲刺灰尘：白色半透明 glow_disc 缩小上飘淡出（仿 FadeOutAndDie 的轻量件模式）。
    /// 面试点：接触面反馈是"角色贴地"的最大来源——落地一蓬尘土、跳起三粒尘，
    /// 玩家瞬间就"感觉到"重量；灰尘全部由本组件自己的 Update 驱动（不为每粒灰尘挂脚本，
    /// 平行列表管理，避免一个 .cs 出现第二个类的脚本绑定坑）。
    /// </summary>
    public class DustPuff : MonoBehaviour
    {
        [Header("灰尘参数")]
        [Tooltip("单粒寿命（秒）")]
        public float lifeTime = 0.3f;
        [Tooltip("上飘速度（世界单位/秒）")]
        public float riseSpeed = 0.9f;
        [Tooltip("起始直径（世界单位，按 glow_disc 精灵实际尺寸换算）")]
        public float puffSize = 0.18f;
        [Tooltip("灰尘颜色（半透明白）")]
        public Color dustColor = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("渲染层级（勇者本体 20 / 残影 19 之下，地面装饰之上）")]
        public int sortingOrder = 18;

        private Sprite _disc;
        private bool _running;
        private float _runTimer;
        private float _dashTimer;
        private float _dashEmitTimer;

        // 活跃灰尘用平行列表驱动（不引入次类）
        private readonly List<SpriteRenderer> _puffs = new();
        private readonly List<Vector2> _velocities = new();
        private readonly List<float> _ages = new();
        private readonly List<float> _sizes = new();

        private void Awake()
        {
            _disc = SpritePool.GlowDisc;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // 奔跑扬尘：接地移动时每 0.15s 一粒（移动状态由 HeroController 每帧喂入）
            if (_running)
            {
                _runTimer -= dt;
                if (_runTimer <= 0f) { _runTimer = 0.15f; Emit(1); }
            }

            // 冲刺尾尘：沿途每 0.03s 一粒
            if (_dashTimer > 0f)
            {
                _dashTimer -= dt;
                _dashEmitTimer -= dt;
                if (_dashEmitTimer <= 0f) { _dashEmitTimer = 0.03f; Emit(1); }
            }

            // 灰尘生命周期：上飘 + 缩小 + 淡出
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                var sr = _puffs[i];
                if (sr == null) { RemoveAt(i); continue; }
                float age = _ages[i] + dt;
                float k = Mathf.Clamp01(age / Mathf.Max(0.01f, lifeTime));
                sr.transform.position += (Vector3)(_velocities[i] * dt);
                float scale = _sizes[i] * (1f - 0.6f * k); // 缩小
                sr.transform.localScale = Vector3.one * scale;
                var c = sr.color;
                c.a = dustColor.a * (1f - k);              // 淡出
                sr.color = c;
                if (k >= 1f)
                {
                    Destroy(sr.gameObject);
                    RemoveAt(i);
                    continue;
                }
                _ages[i] = age;
            }
        }

        private void RemoveAt(int i)
        {
            _puffs.RemoveAt(i);
            _velocities.RemoveAt(i);
            _ages.RemoveAt(i);
            _sizes.RemoveAt(i);
        }

        private void OnDisable()
        {
            // 组件随勇者停用（死亡/换场景）时清场，绝不残留永恒灰尘
            for (int i = _puffs.Count - 1; i >= 0; i--)
                if (_puffs[i] != null) Destroy(_puffs[i].gameObject);
            _puffs.Clear();
            _velocities.Clear();
            _ages.Clear();
            _sizes.Clear();
        }

        // ---------- 事件入口（HeroController 调用） ----------

        /// <summary>喂"是否在接地移动"（内部按 0.15s 节流出粒）。</summary>
        public void SetRunning(bool running) => _running = running;

        /// <summary>起跳扬尘（3 粒）。</summary>
        public void EmitJump() => Emit(3);

        /// <summary>落地扬尘（5 粒）。</summary>
        public void EmitLand() => Emit(5);

        /// <summary>冲刺沿途尾尘（持续 duration，每 0.03s 一粒）。</summary>
        public void DashTrail(float duration)
        {
            _dashTimer = duration;
            _dashEmitTimer = 0f;
        }

        /// <summary>补一口灰尘（攻击滑步等零散用途）。</summary>
        public void Emit(int count)
        {
            for (int i = 0; i < count; i++) SpawnPuff();
        }

        private void SpawnPuff()
        {
            if (_disc == null) _disc = SpritePool.GlowDisc;
            if (_disc == null) return;
            var go = new GameObject("DustPuff");
            go.transform.position = transform.position
                                    + new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(0f, 0.12f), 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _disc;
            sr.color = dustColor;
            sr.sortingOrder = sortingOrder;
            // glow_disc 精灵实际世界尺寸换算（同 BurningZone 的换算方式）
            float spriteWorld = Mathf.Max(0.01f, _disc.bounds.size.x);
            float size = puffSize * Random.Range(0.7f, 1.3f);
            float scaleFactor = size / spriteWorld;
            sr.transform.localScale = Vector3.one * scaleFactor;

            _puffs.Add(sr);
            _velocities.Add(new Vector2(Random.Range(-0.35f, 0.35f), riseSpeed * Random.Range(0.8f, 1.2f)));
            _ages.Add(0f);
            _sizes.Add(scaleFactor);
        }
    }
}
