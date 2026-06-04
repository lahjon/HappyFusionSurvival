using Starter.Common.Input;
using Starter.Shooter;
using UnityEngine;

public class PlayerEmotes : MonoBehaviour
{
    [Tooltip("Animator trigger name to fire when the emote key is pressed.")]
    [SerializeField] private string _emoteTrigger = "WaveRightArm";

    private Player           _player;
    private GameInputActions _input;

    private void Awake()
    {
        _player = GetComponent<Player>();
        _input  = GetComponent<GameInputActions>();
    }

    private void Update()
    {
        if (_input == null || !_input.IsInitialized) return;
        if (_input.Emotes == null)
        {
            Debug.LogWarning("[PlayerEmotes] 'Emote' action not found in the Player input map.");
            return;
        }

        if (_input.Emotes.WasPressedThisFrame())
            TriggerAnimation(_emoteTrigger);
    }

    [Sirenix.OdinInspector.Button]
    public void TriggerAnimation(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName) || _player == null) return;
        //_player.BodyAnimator?.SetTrigger(triggerName);
        //_player.NoHeadAnimator?.SetTrigger(triggerName);
        _player.BodyAnimator?.SetTrigger("WaveRightArm");
        _player.NoHeadAnimator?.SetTrigger("WaveRightArm");
    }
}
