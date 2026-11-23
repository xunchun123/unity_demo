using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 循环序列帧：传送门电光、喷火口火柱这类"一直在播、没有状态切换"的机关表现。
    /// 和 SpriteFx 一样不用 Animator：没有过渡、没有参数，一个计时器换 sprite 就够了。
    /// 起始帧随机错开，同屏多个同类机关不会整齐划一地同步闪。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteLoop : MonoBehaviour
    {
        [SerializeField] private Sprite[] _frames;
        [SerializeField] private float _fps = 12f;

        private SpriteRenderer _sr;
        private float _time;

        public void Configure(Sprite[] frames, float fps)
        {
            _frames = frames;
            _fps = Mathf.Max(1f, fps);
            _sr = GetComponent<SpriteRenderer>();
            if (_frames != null && _frames.Length > 0)
            {
                _time = Random.value * _frames.Length / _fps;
                _sr.sprite = _frames[0];
            }
        }

        private void Awake() => _sr = GetComponent<SpriteRenderer>();

        private void Update()
        {
            if (_frames == null || _frames.Length == 0) return;
            _time += Time.deltaTime;
            _sr.sprite = _frames[Mathf.FloorToInt(_time * _fps) % _frames.Length];
        }
    }
}
