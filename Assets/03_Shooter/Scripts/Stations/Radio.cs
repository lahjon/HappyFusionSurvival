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

		[Networked, OnChangedRender(nameof(OnPlayingChanged))]
		public NetworkBool IsPlaying { get; private set; }

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
			ApplySourceState();
		}

		protected override void OnInteract(InteractionScanner scanner)
		{
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

		private void OnPlayingChanged() => ApplySourceState();

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
