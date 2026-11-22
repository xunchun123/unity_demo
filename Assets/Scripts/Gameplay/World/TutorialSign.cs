using TMPro;
using UnityEngine;

namespace StarVortex.Gameplay.World
{
    /// <summary>
    /// 新手教学牌（只由 LevelBuilder 在 Level1 布置）：
    /// 常驻视觉 = 纵向发光柱（glow_disc 纵向拉伸的暖金光带，远远就能注意到）+ 柱顶双浮动光点
    /// （主点 + 错相位回声点，像一枚呼吸的"!"）；勇者进入触发区 → 柱上方浮出 TMP 中文操作提示
    /// （悬浮微动）。驻留节奏：停在触发区内文字常驻不倒计时，离开才开始倒计时（首见 5 秒）；
    /// 某块牌计满一轮完整展示后，再进入仍会显示但缩短为 1.5 秒——可回看又不反复长弹。
    /// 面试点：教学放进世界里而非开局弹窗——横版节奏是边跑边学，触发式提示不打断操作；
    /// 字体资产由 LevelBuilder 显式注入（项目不依赖 TMP 全局设置，见 EnsureCjkFont 注释）。
    /// 视觉子物体全部在 Start 程序化搭建：场景文件只存触发盒与文案，改文案不用重摆场景。
    /// 层数字契约：触发盒挂 Ground(8) 层走 Player↔Ground 碰撞矩阵（与 SpikeTrap 同契约）。
    /// </summary>
    public class TutorialSign : MonoBehaviour
    {
        [Header("内容（LevelBuilder 注入）")]
        [SerializeField] private string _message = "";
        [SerializeField] private TMP_FontAsset _fontAsset;

        [Header("节奏")]
        [Tooltip("首次提示驻留秒数（玩家离开触发区后开始倒计时，归零淡出；停在区内常驻）")]
        [SerializeField] private float _displaySeconds = 5f;
        [Tooltip("完整展示过一轮后的复读秒数（再进触发区仍显示，但不反复长驻）")]
        [SerializeField] private float _revisitSeconds = 1.5f;
        [Tooltip("淡入淡出速率（alpha 每秒变化量）")]
        [SerializeField] private float _fadeSpeed = 2.8f;

        // 常驻色（HDR > 1 的分量喂 Bloom；alpha 由 ApplyAlpha 按状态接管）
        private static readonly Color BeamColor = new Color(1.35f, 1.05f, 0.45f, 1f);
        private static readonly Color BeaconColor = new Color(1.9f, 1.5f, 0.55f, 1f);
        private static readonly Color EchoColor = new Color(1.4f, 1.1f, 0.45f, 1f);
        private static readonly Color TextColor = new Color(1f, 0.96f, 0.86f, 1f);

        private const float BeamBaseAlpha = 0.18f;   // 光柱底光：未触发也隐约可见（收窄调暗，避免大坨黄斑发糊）
        private const float BeamLitAlpha = 0.75f;    // 触发后的光柱上限：亮到能引导、不过曝成糊斑
        private const float BeaconBaseAlpha = 0.55f; // 光点底光：远看先看到"这里有东西"

        private Sprite _glowSprite;
        private SpriteRenderer _beam;
        private SpriteRenderer _beacon;
        private SpriteRenderer _beaconEcho;
        private TextMeshPro _label;
        private float _alpha;        // 当前提示透明度 0..1
        private float _alphaTarget;
        private float _holdTimer;    // 驻留倒计时（只在离开触发区后走，区内冻结）
        private float _bobSeed;      // 各牌相位错开，避免整排光点同步跳动
        private bool _playerInside;  // 勇者是否仍在触发区内
        private bool _fullShown;     // 已计满一轮完整展示（之后复读缩短）
        private bool _built;

        /// <summary>注入文案与中文字体资产（LevelBuilder 建场景时调用）。</summary>
        public void Configure(string message, TMP_FontAsset fontAsset)
        {
            _message = message;
            _fontAsset = fontAsset;
        }

        private void Start()
        {
            _bobSeed = transform.position.x * 0.37f;
            BuildVisuals();
        }

        private void Update()
        {
            if (!_built) return;

            // 驻留倒计时只在离开触发区后走：玩家停在区内提示常驻（离开才开始淡出计时）；
            // 归零时标记"已完整看过一轮"，之后复读缩短，避免反复路过反复长弹
            if (!_playerInside && _holdTimer > 0f)
            {
                _holdTimer -= Time.deltaTime;
                if (_holdTimer <= 0f)
                {
                    _alphaTarget = 0f;
                    _fullShown = true;
                }
            }
            _alpha = Mathf.MoveTowards(_alpha, _alphaTarget, _fadeSpeed * Time.deltaTime);
            ApplyAlpha();
            AnimateBob();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<HeroController>() == null) return;
            _playerInside = true;
            _holdTimer = _fullShown ? _revisitSeconds : _displaySeconds;
            _alphaTarget = 1f;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponentInParent<HeroController>() == null) return;
            _playerInside = false; // 不立刻收字：由 Update 继续走驻留倒计时，归零才淡出
        }

        // ---------- 程序化视觉 ----------

        private void BuildVisuals()
        {
            _built = true;
            // Resources 通道由 FantasyArtGenerator 维护（glow_disc 已复制进 Assets/Resources/Sprites）
            _glowSprite = Resources.Load<Sprite>("Sprites/glow_disc");
            if (_glowSprite == null)
                Debug.LogWarning("[TutorialSign] 缺 Resources/Sprites/glow_disc——光柱/光点不可见，文字提示照常工作");

            // 发光柱：glow_disc 纵向拉伸成柔和光带（2.56u 图 → 约 0.77 × 4.7u），底端沉入地面；
            // 横向收窄到 0.30——旧 0.42 的大坨黄斑糊成一团，细一点更像"光柱"也更好读
            var beamGo = new GameObject("Beam");
            beamGo.transform.SetParent(transform, false);
            beamGo.transform.localPosition = new Vector3(0f, 2.25f, 0f);
            beamGo.transform.localScale = new Vector3(0.30f, 1.85f, 1f);
            _beam = beamGo.AddComponent<SpriteRenderer>();
            _beam.sprite = _glowSprite;
            _beam.color = BeamColor;
            _beam.sortingOrder = -3; // 压在玩法对象之下，光带只是背景引导

            // 柱顶浮动光点（"!"主点）+ 上方小回声点（错相位浮动）
            _beacon = MakeDot("Beacon", new Vector3(0f, 3.15f, 0f), 0.34f, BeaconColor);
            _beaconEcho = MakeDot("BeaconEcho", new Vector3(0f, 3.62f, 0f), 0.18f, EchoColor);

            // 提示文字：世界空间 TMP，中文字体由 LevelBuilder 注入；初始 alpha 0，触发后浮出
            var labelGo = new GameObject("Tip");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 4.05f, 0f);
            _label = labelGo.AddComponent<TextMeshPro>();
            _label.text = _message;
            if (_fontAsset != null) _label.font = _fontAsset;
            _label.fontSize = 3.4f; // 旧 2.6 偏小（新手反馈看不清），提升 ~30%；两行文案也按此字号排
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontStyle = FontStyles.Bold;
            _label.enableWordWrapping = false;
            _label.overflowMode = TextOverflowModes.Overflow;
            _label.autoSizeTextContainer = true;
            _label.color = TextColor;
            var labelRenderer = _label.GetComponent<MeshRenderer>();
            if (labelRenderer != null) labelRenderer.sortingOrder = 30; // 玩法层之上最可读；暗角同值但中心透明，不冲突

            ApplyAlpha(); // 初始态：光柱/光点带底光，提示文字隐藏
        }

        private SpriteRenderer MakeDot(string name, Vector3 pos, float scale, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _glowSprite;
            sr.color = color;
            sr.sortingOrder = 18; // 怪物视觉(15)之上、勇者(20)之下：亮面浮在怪群上、不遮主角
            return sr;
        }

        // ---------- 状态应用 ----------

        private void ApplyAlpha()
        {
            SetAlpha(_beam, BeamBaseAlpha + (BeamLitAlpha - BeamBaseAlpha) * _alpha);
            SetAlpha(_beacon, BeaconBaseAlpha + (1f - BeaconBaseAlpha) * _alpha);
            SetAlpha(_beaconEcho, 0.4f + 0.6f * _alpha);
            if (_label != null) _label.color = new Color(TextColor.r, TextColor.g, TextColor.b, _alpha);
        }

        private static void SetAlpha(SpriteRenderer sr, float a)
        {
            if (sr == null) return;
            var c = sr.color;
            c.a = a;
            sr.color = c;
        }

        private void AnimateBob()
        {
            float t = Time.time * 2.1f + _bobSeed;
            if (_beacon != null)
                _beacon.transform.localPosition = new Vector3(0f, 3.15f + Mathf.Sin(t) * 0.09f, 0f);
            if (_beaconEcho != null)
                _beaconEcho.transform.localPosition = new Vector3(0f, 3.62f + Mathf.Sin(t * 1.35f + 1.7f) * 0.07f, 0f);
            if (_label != null)
                _label.transform.localPosition = new Vector3(0f, 4.05f + Mathf.Sin(t * 0.8f) * 0.05f, 0f);
        }
    }
}
