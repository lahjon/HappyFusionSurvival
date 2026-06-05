using System.Collections;
using Starter.Common.Input;
using Starter.Shooter;
using UnityEngine;

public class PlayerEmotes : MonoBehaviour
{
    [Tooltip("All EmoteData assets. Used by the emote wheel and for audio RPC index lookup.")]
    public EmoteData[] Emotes = new EmoteData[0];

    [Tooltip("EmoteWheel prefab — instantiated automatically at runtime, no need to place in every scene.")]
    [SerializeField] private EmoteWheel _emoteWheelPrefab;

    private Player           _player;
    private GameInputActions _input;
    private Coroutine        _restoreCoroutine;
    private EmoteWheel       _emoteWheelInstance;

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
        {
            var wheel = GetOrCreateWheel();
            if (wheel == null) return;
            if (wheel.IsOpen) wheel.CloseFromMenu();
            else              wheel.Open(this);
        }
    }

    /// <summary>Play an emote by definition. Safe to call from the emote wheel or any other system.</summary>
    private EmoteWheel GetOrCreateWheel()
    {
        if (_emoteWheelInstance != null) return _emoteWheelInstance;
        if (_emoteWheelPrefab   == null) return null;
        _emoteWheelInstance = Instantiate(_emoteWheelPrefab);
        return _emoteWheelInstance;
    }

    [Sirenix.OdinInspector.Button]
    public void PlayEmote(EmoteData emote)
    {
        if (emote == null || emote.Clip == null || _player == null) return;
        if (_player.IsEmoting) return;

        _player.BodyAnimator?.SetTrigger(emote.TriggerName);
        _player.NoHeadAnimator?.SetTrigger(emote.TriggerName);
        if (_player.BodyAnimator   != null) _player.BodyAnimator.speed   = emote.AnimationSpeed;
        if (_player.NoHeadAnimator != null) _player.NoHeadAnimator.speed = emote.AnimationSpeed;

        if (_restoreCoroutine != null) StopCoroutine(_restoreCoroutine);
        _restoreCoroutine = StartCoroutine(Suppress(emote.Duration, emote));
    }

    private IEnumerator Suppress(float duration, EmoteData emote)
    {
        _player.SetIKSuppressedByEmote(true);
        SetHandItemVisible(false);

        if (emote.EmoteAudio != null)
        {
            int index = System.Array.IndexOf(Emotes, emote);
            if (index >= 0)
                _player.PlayEmoteAudioForAll(index, emote.AudioDelay);
        }


        if (emote.ShowFullBody)
        {
            // Cache where the player was looking so we can snap back after.
            UnityEngine.Vector2 lookBefore = _player.Input != null
                ? _player.Input.LookRotation
                : UnityEngine.Vector2.zero;

            _player.ShowFullBodyForEmote = true;
            yield return LerpCameraOffset(emote.ThirdPersonOffset, emote.CameraTransitionSpeed);

            yield return new UnityEngine.WaitForSeconds(duration);

            yield return LerpCameraOffset(UnityEngine.Vector3.zero, emote.CameraTransitionSpeed);

            // Snap look rotation back so the player faces the same direction as before the orbit.
            _player.Input?.SetLookRotation(lookBefore);
            _player.ShowFullBodyForEmote = false;
        }
        else
        {
            yield return new UnityEngine.WaitForSeconds(duration);
        }

        if (_player.BodyAnimator   != null) _player.BodyAnimator.speed   = 1f;
        if (_player.NoHeadAnimator != null) _player.NoHeadAnimator.speed = 1f;
        _player.SetIKSuppressedByEmote(false);
        SetHandItemVisible(true);
        _restoreCoroutine = null;
    }

    private System.Collections.IEnumerator LerpCameraOffset(Vector3 target, float speed)
    {
        while ((_player.EmoteCameraOffset - target).sqrMagnitude > 0.001f)
        {
            _player.EmoteCameraOffset = Vector3.MoveTowards(
                _player.EmoteCameraOffset, target, speed * Time.deltaTime);
            yield return null;
        }
        _player.EmoteCameraOffset = target;
    }

    private void SetHandItemVisible(bool visible)
    {
        if (_player?.HandItemAnchor != null)
            _player.HandItemAnchor.gameObject.SetActive(visible);
    }
}
