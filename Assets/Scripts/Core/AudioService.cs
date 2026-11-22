using System.Collections.Generic;
using UnityEngine;

namespace StarVortex.Core
{
    /// <summary>音效种类（AudioClip 由构建脚本生成/注入）。</summary>
    public enum SfxId { Shoot, Hit, CritHit, Kill, EliteKill, Pickup, LevelUp, PlayerHurt, GameOver, UiClick }

    /// <summary>
    /// 音频服务：轮换 AudioSource 播放短音效 + 音高随机化。
    /// 面试点：音高 ±8% 随机化是廉价防疲劳技巧——同一音效连听几十次也不吵；
    /// 多个 AudioSource 轮换避免同帧多音效互相打断。
    /// </summary>
    public class AudioService : MonoBehaviour
    {
        public static AudioService Instance { get; private set; }

        [Header("音效资产（构建脚本注入，程序化合成 WAV）")]
        [SerializeField] private List<AudioClip> _clips = new();
        [SerializeField] private AudioSource _musicSource;
        [SerializeField] private float sfxVolume = 0.35f;
        [SerializeField] private float musicVolume = 0.30f;

        private readonly List<AudioSource> _sfxSources = new();
        private int _nextSource;
        private AudioClip[] _indexed;

        [SerializeField] private bool _bound; // 序列化防重复绑定（编辑器期绑定过则运行时跳过）

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 编辑器期 BindClips 只序列化了 _clips/_musicSource；_indexed 与 _sfxSources 是运行时字段，
            // 跨场景恢复后必须重建，否则打包版 IsBound 恒 false、整局无声
            if (_bound && _indexed == null && _clips.Count > 0)
            {
                _indexed = _clips.ToArray();
                _sfxSources.Clear();
                foreach (var src in GetComponents<AudioSource>())
                    if (src != _musicSource) _sfxSources.Add(src);
                if (_sfxSources.Count == 0) CreateSfxSources(6);
                if (_musicSource != null && _musicSource.clip != null)
                {
                    _musicSource.loop = true;
                    _musicSource.volume = musicVolume;
                    _musicSource.Play();
                }
            }
        }

        private void CreateSfxSources(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.volume = sfxVolume;
                _sfxSources.Add(src);
            }
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>是否已绑定音效（Menu/Game 两个场景都调用，防重复绑定）。</summary>
        public bool IsBound => _bound && _indexed != null && _indexed.Length > 0;

        /// <summary>按 SfxId 顺序绑定的 Clip（构建脚本保证顺序与 SfxId 枚举一致）。</summary>
        public void BindClips(List<AudioClip> clips, AudioClip music, int sourceCount = 6)
        {
            if (IsBound) return;
            _bound = true;
            if (_clips.Count == 0) _clips.AddRange(clips); // 场景里已序列化过就不再叠加（重入会错位索引）
            _indexed = _clips.ToArray();
            _sfxSources.Clear();
            CreateSfxSources(sourceCount);
            if (music != null)
            {
                _musicSource = gameObject.AddComponent<AudioSource>();
                _musicSource.clip = music;
                _musicSource.loop = true;
                _musicSource.volume = musicVolume;
                _musicSource.Play();
            }
        }

        public void Play(SfxId id, float pitchJitter = 0.08f)
        {
            int idx = (int)id;
            if (_indexed == null || idx < 0 || idx >= _indexed.Length || _indexed[idx] == null) return;

            var src = _sfxSources[_nextSource];
            _nextSource = (_nextSource + 1) % _sfxSources.Count;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.PlayOneShot(_indexed[idx]);
        }

        /// <summary>指定音高播放（拾取连击递增用）。下一次 Play 会重置轮换槽的 pitch。</summary>
        public void PlayWithPitch(SfxId id, float pitch)
        {
            int idx = (int)id;
            if (_indexed == null || idx < 0 || idx >= _indexed.Length || _indexed[idx] == null) return;

            var src = _sfxSources[_nextSource];
            _nextSource = (_nextSource + 1) % _sfxSources.Count;
            src.pitch = pitch;
            src.PlayOneShot(_indexed[idx]);
        }

        public void PlayUiClick() => Play(SfxId.UiClick, pitchJitter: 0f);
    }
}
