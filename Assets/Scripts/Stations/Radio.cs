using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked radio appliance. Like the <see cref="Microwave"/>, it's a fixed station with all
	/// interactions on physical button children (a <see cref="RadioButton"/> wired via its kind to
	/// one of the public request methods here) — the body itself is not interactable:
	///
	/// - <b>Power button</b> → <see cref="RequestTogglePlay"/>: toggles playback for all peers.
	/// - <b>Next-song button</b> → <see cref="RequestNextSong"/>: advances to the next clip in
	///   <see cref="_songs"/> (wraps around), synced for everyone.
	///
	/// Sync model (per CLAUDE.md): <see cref="IsPlaying"/> and <see cref="SongIndex"/> are single
	/// <c>[Networked]</c> values driven by state-authority RPCs and applied on every peer via
	/// <c>OnChangedRender</c> — tolerates lost ticks better than RPC-to-All for a cosmetic
	/// on/off + clip-select effect.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(AudioSource))]
	public sealed class Radio : NetworkBehaviour
	{
		[Header("Audio")]
		[Tooltip("AudioSource driven by the networked state. Author loop / volume here — Radio sets the clip from the song list and only calls Play()/Stop().")]
		[SerializeField] private AudioSource _source;

		[Tooltip("Songs the Next button cycles through. When empty, the clip authored on the AudioSource is used as-is.")]
		[SerializeField] private AudioClip[] _songs;

		[Header("Authority")]
		[Tooltip("Host re-validates the requesting player is within this range before applying a button press. 0 = skip the check.")]
		[Min(0f)] [SerializeField] private float _hostValidationRange = 2.5f;

		[Networked, OnChangedRender(nameof(OnPlayingChanged))]
		public NetworkBool IsPlaying { get; private set; }

		/// <summary>Index into <see cref="_songs"/> of the current track. State authority writes; peers render via OnChangedRender.</summary>
		[Networked, OnChangedRender(nameof(OnSongChanged))]
		public int SongIndex { get; private set; }

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
			ApplyClip();
			ApplySourceState();
		}

		// =====================================================================
		// Drive points — wired from the RadioButton IInteractable children
		// =====================================================================

		/// <summary>Power button. Toggles playback; sends a state-authority RPC.</summary>
		public void RequestTogglePlay() => RPC_RequestToggle();

		/// <summary>Next-song button. Advances to the next clip; sends a state-authority RPC.</summary>
		public void RequestNextSong() => RPC_RequestNextSong();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestToggle(RpcInfo info = default)
		{
			if (!ValidateSource(info)) return;
			IsPlaying = !IsPlaying;
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestNextSong(RpcInfo info = default)
		{
			if (!ValidateSource(info)) return;
			if (_songs == null || _songs.Length == 0) return;

			SongIndex = (SongIndex + 1) % _songs.Length;
		}

		private bool ValidateSource(RpcInfo info)
		{
			if (Runner == null) return false;
			if (_hostValidationRange <= 0f) return true;

			var src = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			var playerObj = Runner.GetPlayerObject(src);
			if (playerObj == null) return false;

			float allowed = _hostValidationRange * 1.25f;
			return (transform.position - playerObj.transform.position).sqrMagnitude <= allowed * allowed;
		}

		private void OnPlayingChanged() => ApplySourceState();

		private void OnSongChanged()
		{
			ApplyClip();
			// If we're mid-playback, restart on the new clip so the change is audible immediately.
			if (IsPlaying && _source != null)
			{
				_source.Stop();
				_source.Play();
			}
		}

		private void ApplyClip()
		{
			if (_source == null) return;
			if (_songs == null || _songs.Length == 0) return;

			int idx = Mathf.Clamp(SongIndex, 0, _songs.Length - 1);
			if (_source.clip != _songs[idx]) _source.clip = _songs[idx];
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
