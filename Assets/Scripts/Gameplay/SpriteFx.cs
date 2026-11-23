using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 一次性序列帧特效（怪物死亡爆散等）：按帧率换 sprite，播完自毁。
    /// 用 SpriteRenderer + 计时器而不是 Animator：特效没有状态切换，只是播一遍，
    /// 每个特效实例挂一个 Animator 控制器是过度设计。
    /// </summary>
    public class SpriteFx : MonoBehaviour
    {
        private Sprite[] _frames;
        private float _fps;
        private float _time;
        private SpriteRenderer _sr;

        public static SpriteFx Spawn(Sprite[] frames, Vector3 position, float fps = 14f, float scale = 1f,
            int sortingOrder = 30, bool flipX = false)
        {
            if (frames == null || frames.Length == 0) return null;
            var go = new GameObject("SpriteFx");
            go.transform.position = position;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var fx = go.AddComponent<SpriteFx>();
            fx._frames = frames;
            fx._fps = Mathf.Max(1f, fps);
            fx._sr = go.AddComponent<SpriteRenderer>();
            fx._sr.sprite = frames[0];
            fx._sr.sortingOrder = sortingOrder;
            fx._sr.flipX = flipX;
            return fx;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            int frame = Mathf.FloorToInt(_time * _fps);
            if (frame >= _frames.Length)
            {
                Destroy(gameObject);
                return;
            }
            _sr.sprite = _frames[frame];
        }
    }
}
