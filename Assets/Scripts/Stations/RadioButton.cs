using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// One physical button on a <see cref="Radio"/>. A local <see cref="IInteractable"/> — the
	/// radio itself owns every bit of networked state, so the button only forwards a tap to it.
	/// Mirrors <see cref="MicrowaveButton"/>. Two of these live on the prefab (one
	/// <see cref="ButtonKind.TogglePlay"/> power button, one <see cref="ButtonKind.NextSong"/>) so
	/// the <see cref="InteractionScanner"/> can target each independently by look-alignment.
	///
	/// Local-only by design (plain MonoBehaviour): pressing it calls the owning radio's public
	/// request method (which sends the state-authority RPC), plays an optional click, and dips the
	/// button mesh inward for tactile feedback. Add an <see cref="InteractionPrompt"/> sibling for
	/// the on-screen prompt.
	/// </summary>
	[RequireComponent(typeof(Collider))]
	public sealed class RadioButton : MonoBehaviour, IInteractable
	{
		public enum ButtonKind { TogglePlay, NextSong }

		[Tooltip("Owning radio. Auto-found in parents on Reset.")]
		[SerializeField] private Radio _radio;

		[Tooltip("Which radio action this button triggers.")]
		[SerializeField] private ButtonKind _kind = ButtonKind.TogglePlay;

		[Min(0f)] [SerializeField] private float _interactRange = 2.5f;

		[Tooltip("Prompt label shown when aiming at this button. For the power button this is the idle/'Play' label.")]
		[SerializeField] private string _label = "Play";

		[Tooltip("Power button only: prompt label shown while the radio is playing (acts as the Stop label).")]
		[SerializeField] private string _playingLabel = "Stop";

		[Header("Press feedback")]
		[Tooltip("Optional button mesh pushed inward briefly on press. Local/cosmetic only; defaults to this transform on Reset.")]
		[SerializeField] private Transform _buttonMesh;

		[Tooltip("Local-space direction the button travels when pressed (into the radio body). Normalized at runtime.")]
		[SerializeField] private Vector3 _pressAxis = new Vector3(0f, 0f, -1f);

		[Tooltip("How far the button sinks at the deepest point of the press, in the mesh's parent-local units.")]
		[Min(0f)] [SerializeField] private float _pressDepth = 0.15f;

		[Tooltip("Seconds for the full press-and-release dip.")]
		[Min(0.01f)] [SerializeField] private float _pressDuration = 0.12f;

		private Vector3 _buttonRest;
		private float _pressTimer;

		private void Reset()
		{
			_radio = GetComponentInParent<Radio>();
			_buttonMesh = transform;
		}

		private void Awake()
		{
			if (_buttonMesh != null) _buttonRest = _buttonMesh.localPosition;
		}

		private void Update()
		{
			if (_buttonMesh == null) return;

			if (_pressTimer > 0f)
			{
				_pressTimer -= Time.deltaTime;
				// Ease in and back out: push peaks at the midpoint of the press window.
				float progress = Mathf.Clamp01(1f - _pressTimer / _pressDuration);
				float push = Mathf.Sin(progress * Mathf.PI);
				_buttonMesh.localPosition = _buttonRest + _pressAxis.normalized * (_pressDepth * push);
			}
			else if (_buttonMesh.localPosition != _buttonRest)
			{
				_buttonMesh.localPosition = _buttonRest;
			}
		}

		// --- IInteractable ---

		float IInteractable.InteractRange => _interactRange;
		bool IInteractable.CanInteract => _radio != null && isActiveAndEnabled;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => string.Empty;
		// Power button flips its label between "Play" and "Stop" as the radio plays;
		// read live each frame by the InteractionScanner/prompt (the MicrowaveButton pattern).
		string IInteractable.InteractLabel =>
			_kind == ButtonKind.TogglePlay && _radio != null && _radio.IsPlaying
				? _playingLabel
				: _label;

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			if (_radio == null) return;

			// Immediate local feedback — the networked flip arrives a few ticks later. The
			// interaction sound (if any) is played centrally by the InteractionScanner from the
			// InteractionPrompt sibling, so the button only owns the visual press dip here.
			if (_buttonMesh != null && _pressTimer <= 0f) _pressTimer = _pressDuration;

			switch (_kind)
			{
				case ButtonKind.TogglePlay: _radio.RequestTogglePlay(); break;
				case ButtonKind.NextSong:   _radio.RequestNextSong();   break;
			}
		}
	}
}
