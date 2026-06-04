using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked placeable radio. Tap Interact toggles playback for all peers;
	/// hold Interact picks it up (handled by <see cref="InteractableStation"/>) —
	/// despawning the radio also tears down the local <see cref="AudioSource"/>,
	/// which is what "stops playing on pickup" gets us for free.
	///
	/// Sync model: a single <c>[Networked]</c> bool driven via state-authority RPC,
	/// with <c>OnChangedRender</c> starting/stopping the local source. Per
	/// CLAUDE.md, this tolerates lost ticks better than an RPC-to-All for a
	/// cosmetic on/off effect.
	/// </summary>
	[RequireComponent(typeof(AudioSource))]
	public sealed class Radio : InteractableStation
	{
		[Header("Audio")]
		[Tooltip("AudioSource driven by the networked IsPlaying flag. Author the clip / loop / volume on this source — Radio only calls Play()/Stop().")]
		[SerializeField] private AudioSource _source;

		[Header("Button feedback")]
		[Tooltip("The physical on/off button mesh. Pushed inward briefly each time playback is toggled. Purely local/cosmetic — toggling is networked via IsPlaying.")]
		[SerializeField] private Transform _button;

		[Tooltip("Local-space direction the button travels when pressed (into the radio body). Normalized at runtime.")]
		[SerializeField] private Vector3 _pressAxis = new Vector3(0f, 0f, -1f);

		[Tooltip("How far the button sinks at the deepest point of the press, in the button's parent-local units.")]
		[Min(0f)] [SerializeField] private float _pressDepth = 0.15f;

		[Tooltip("Seconds for the full press-and-release dip.")]
		[Min(0.01f)] [SerializeField] private float _pressDuration = 0.12f;

		[Networked, OnChangedRender(nameof(OnPlayingChanged))]
		public NetworkBool IsPlaying { get; private set; }

		private Vector3 _buttonRest;
		private float _pressTimer;

		private void Reset()
		{
			_source = GetComponent<AudioSource>();
			if (_source != null)
			{
				_source.playOnAwake = false;
				_source.loop = true;
			}
		}

		public override void Spawned()
		{
			if (_source == null) _source = GetComponent<AudioSource>();
			if (_button != null) _buttonRest = _button.localPosition;
			ApplySourceState();
		}

		private void Update()
		{
			if (_button == null) return;

			if (_pressTimer > 0f)
			{
				_pressTimer -= Time.deltaTime;
				// Ease in and back out: push peaks at the midpoint of the press window.
				float progress = Mathf.Clamp01(1f - _pressTimer / _pressDuration);
				float push = Mathf.Sin(progress * Mathf.PI);
				_button.localPosition = _buttonRest + _pressAxis.normalized * (_pressDepth * push);
			}
			else if (_button.localPosition != _buttonRest)
			{
				_button.localPosition = _buttonRest;
			}
		}

		/// <summary>Kicks off the local inward-push dip. Ignored while a dip is already playing so it can't pop mid-animation.</summary>
		private void PlayPressFeedback()
		{
			if (_button != null && _pressTimer <= 0f) _pressTimer = _pressDuration;
		}

		protected override void OnInteract(InteractionScanner scanner)
		{
			// Immediate local feedback — the networked flip arrives a few ticks later.
			PlayPressFeedback();
			RPC_RequestToggle();
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestToggle(RpcInfo info = default)
		{
			if (Runner == null) return;

			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			IsPlaying = !IsPlaying;
		}

		private void OnPlayingChanged()
		{
			// Remote peers (and the local presser, if RTT outran the local dip) see the button press.
			PlayPressFeedback();
			ApplySourceState();
		}

		private void ApplySourceState()
		{
			if (_source == null) return;
			if (IsPlaying)
			{
				if (!_source.isPlaying) _source.Play();
			}
			else
			{
				if (_source.isPlaying) _source.Stop();
			}
		}
	}
}
