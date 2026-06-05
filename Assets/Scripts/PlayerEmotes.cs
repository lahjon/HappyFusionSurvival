using System.Collections;
using Starter.Common.Input;
using Starter.Shooter;
using UnityEngine;

public class PlayerEmotes : MonoBehaviour
{
    [Tooltip("Default emote played when the emote key is pressed.")]
    [SerializeField] private EmoteData _defaultEmote;

    private Player           _player;
    private GameInputActions _input;
    private Coroutine        _restoreCoroutine;

    private void Awake()
    {
        _player = GetComponent<Player>();
        _input  = GetComponent<GameInputActions>();
    }

    private void Update()
    {
        if (_input == null || !_input.IsInitialized) return;
        if (_input.Emotes == null) return;
        if (_input.Emotes.WasPressedThisFrame())
            PlayEmote(_defaultEmote);
    }

    /// <summary>Play an emote by definition. Safe to call from the emote wheel or any other system.</summary>
    [Sirenix.OdinInspector.Button]
    public void PlayEmote(EmoteData emote)
    {
        if (emote == null || emote.Clip == null || _player == null) return;
        if (_player.IsEmoting) return;

        _player.BodyAnimator?.SetTrigger(emote.TriggerName);
        _player.NoHeadAnimator?.SetTrigger(emote.TriggerName);

        if (_restoreCoroutine != null) StopCoroutine(_restoreCoroutine);
        _restoreCoroutine = StartCoroutine(Suppress(emote.Duration));
    }

    private IEnumerator Suppress(float duration)
    {
        _player.SetIKSuppressedByEmote(true);
        SetHandItemVisible(false);

        yield return new WaitForSeconds(duration);

        _player.SetIKSuppressedByEmote(false);
        SetHandItemVisible(true);
        _restoreCoroutine = null;
    }

    private void SetHandItemVisible(bool visible)
    {
        if (_player?.HandItemAnchor != null)
            _player.HandItemAnchor.gameObject.SetActive(visible);
    }
}
