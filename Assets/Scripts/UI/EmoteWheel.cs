using System.Collections.Generic;
using Starter.Common.Menu;
using Starter.Shooter;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
    /// <summary>
    /// Radial emote wheel. Opens on Z, shows all EmoteData assets, click to play.
    /// Registers on the MenuManager stack so Escape closes it properly.
    /// </summary>
    public class EmoteWheel : MonoBehaviour, IMenuScreen
    {
        [Header("References")]
        [SerializeField] private GameObject      _root;
        [SerializeField] private Transform       _buttonContainer;
        [SerializeField] private GameObject      _buttonPrefab;

        [Header("Emotes")]
        [SerializeField] private List<EmoteData> _emotes = new();

        // ── IMenuScreen ──────────────────────────────────────────────────────
        public string MenuName        => "EmoteWheel";
        public bool   DismissOnEscape => true;
        public bool   IsOpen          => _root != null && _root.activeSelf;

        private PlayerEmotes _playerEmotes;
        private bool         _built;

        // ── Lifecycle ────────────────────────────────────────────────────────
        private void Awake()
        {
            _root.SetActive(false);
        }

        // ── Open / Close ─────────────────────────────────────────────────────
        public void Open(PlayerEmotes playerEmotes)
        {
            if (MenuManager.Instance != null && MenuManager.Instance.IsAnyOpen) return;

            _playerEmotes = playerEmotes;
            if (!_built) BuildButtons();

            _root.SetActive(true);
            MenuManager.Instance?.Open(this);

            // Unlock cursor so the player can click
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        public void CloseFromMenu()
        {
            _root.SetActive(false);
            MenuManager.Instance?.Close(this);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        // ── Button building ───────────────────────────────────────────────────
        private void BuildButtons()
        {
            _built = true;
            foreach (var emote in _emotes)
            {
                if (emote == null) continue;
                var go   = Instantiate(_buttonPrefab, _buttonContainer);
                var btn  = go.GetComponent<Button>();
                var icon = go.transform.Find("Icon")?.GetComponent<Image>();
                var lbl  = go.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();

                if (icon != null && emote.Icon != null) icon.sprite = emote.Icon;
                if (lbl  != null) lbl.text = emote.DisplayName;

                var captured = emote;
                btn.onClick.AddListener(() => OnEmoteClicked(captured));
            }
        }

        private void OnEmoteClicked(EmoteData emote)
        {
            CloseFromMenu();
            _playerEmotes?.PlayEmote(emote);
        }
    }
}
