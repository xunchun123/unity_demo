using System.Collections.Generic;
using StarVortex.Core;
using StarVortex.Data;
using StarVortex.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StarVortex.UI
{
    /// <summary>
    /// 升级三选一弹窗：timeScale=0 下工作，卡片弹入 + 键盘/鼠标双通道选择。
    /// 面试点：暂停态下 UI 动画必须跑在 unscaled time 上（本项目的 Tween 默认 unscaled），
    /// 这是"暂停弹窗动画冻结"这一经典 bug 的正确解法。
    /// </summary>
    public class LevelUpPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Transform _cardsRoot;
        [SerializeField] private LevelUpCard _cardPrefab;
        [SerializeField] private TMP_Text _headerLabel;
        [SerializeField] private UpgradeData[] _upgradePool; // 由构建脚本注入升级资产

        private readonly List<LevelUpCard> _spawnedCards = new();
        private PlayerController _player;

        private void Awake()
        {
            gameObject.SetActive(false);
            _group.alpha = 0f;
        }

        private void OnEnable()
        {
            GameEvents.LevelUp += OnLevelUp;
        }

        private void OnDisable()
        {
            GameEvents.LevelUp -= OnLevelUp;
        }

        private void OnLevelUp(int newLevel)
        {
            _headerLabel.text = $"等级 {newLevel}！选择一项强化";
            Open();
        }

        /// <summary>打开弹窗：抽 3 个不重复的升级项。</summary>
        private void Open()
        {
            ClearCards();
            _group.alpha = 1f;
            gameObject.SetActive(true);

            var picks = RollThree();
            for (int i = 0; i < picks.Count; i++)
            {
                var card = Instantiate(_cardPrefab, _cardsRoot);
                card.Init(picks[i], i, OnPicked);
                Tween.ScaleIn(card, card.transform, 1f, 0.28f); // unscaled 弹入
                _spawnedCards.Add(card);
            }

            AudioService.Instance?.Play(SfxId.LevelUp);
            Juice.LevelUpRing(FindFirstObjectByType<PlayerController>().transform.position);
        }

        /// <summary>不放回抽取 3 个升级（简单洗牌法，避免同帧随机重复）。</summary>
        private List<UpgradeData> RollThree()
        {
            var pool = new List<UpgradeData>(_upgradePool);
            var picks = new List<UpgradeData>(3);
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int idx = Random.Range(0, pool.Count);
                picks.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return picks;
        }

        private void OnPicked(UpgradeData picked)
        {
            _player ??= FindFirstObjectByType<PlayerController>();
            _player.Stats.ApplyUpgrade(picked);
            // MaxHp 升级顺手回满差额（体验细节：升级应该立刻感觉到变强）
            if (picked.kind == UpgradeKind.MaxHpAdd) _player.Heal(picked.value);
            AudioService.Instance?.PlayUiClick();

            _player.Stats.ConsumePendingLevelUp();
            Close();
        }

        private void Close()
        {
            ClearCards();
            gameObject.SetActive(false);
            GameManager.Instance.FinishLevelUp();
        }

        private void ClearCards()
        {
            foreach (var card in _spawnedCards) Destroy(card.gameObject);
            _spawnedCards.Clear();
        }

        private void Update()
        {
            // 键盘 1/2/3 快捷选择（老玩家操作流）
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame && _spawnedCards.Count > 0) OnPicked(_spawnedCards[0].Data);
            if (kb.digit2Key.wasPressedThisFrame && _spawnedCards.Count > 1) OnPicked(_spawnedCards[1].Data);
            if (kb.digit3Key.wasPressedThisFrame && _spawnedCards.Count > 2) OnPicked(_spawnedCards[2].Data);
        }
    }
}
