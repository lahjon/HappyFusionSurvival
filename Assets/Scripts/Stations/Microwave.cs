using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked microwave with two physical buttons (each a sibling <c>BaseInteractable</c>
	/// in the prefab, wired via its <c>OnInteracted</c> UnityEvent to the public methods here):
	///
	/// - <b>Door button</b> → <see cref="RequestToggleDoor"/>: opens / closes the door. Opening
	///   the door mid-run aborts the cook (no finish "pling"), like a real microwave's safety cut-off.
	/// - <b>Start button</b> → <see cref="RequestStart"/>: runs the microwave for <see cref="_runSeconds"/>.
	///   Ignored while the door is open or while already running.
	///
	/// While running, a looping hum <see cref="AudioSource"/> plays and the interior
	/// <see cref="Light"/> is on. When the timer elapses naturally, a one-shot "pling" fires.
	///
	/// Sync model (per CLAUDE.md):
	/// - <see cref="IsRunning"/> / <see cref="DoorOpen"/>: single <c>[Networked]</c> bools driven by
	///   state-authority RPCs, applied on every peer via <c>OnChangedRender</c> (the <c>Radio</c> pattern).
	/// - <see cref="RunTimer"/>: a <c>[Networked] TickTimer</c> the state authority watches in
	///   <see cref="FixedUpdateNetwork"/> — never <c>Time.time</c>.
	/// - The finish "pling" is a <c>[Networked]</c> counter + <c>OnChangedRender</c> (one-shot synced
	///   effect; tolerates lost ticks better than an RPC-to-All).
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class Microwave : NetworkBehaviour
	{
		[Header("Run")]
		[Tooltip("How long a cook cycle runs, in seconds.")]
		[Min(0.1f)] [SerializeField] private float _runSeconds = 10f;

		[Header("Door")]
		[Tooltip("Door transform that swings on the hinge. Lerped locally toward the open/closed pose.")]
		[SerializeField] private Transform _door;
		[Tooltip("Local euler angles of the door when closed.")]
		[SerializeField] private Vector3 _doorClosedEuler = Vector3.zero;
		[Tooltip("Local euler angles of the door when open.")]
		[SerializeField] private Vector3 _doorOpenEuler = new Vector3(0f, -110f, 0f);
		[Tooltip("How fast the door swings toward its target pose.")]
		[Min(0.1f)] [SerializeField] private float _doorLerpSpeed = 10f;

		[Header("Running FX")]
		[Tooltip("Interior light, enabled only while running.")]
		[SerializeField] private Light _light;
		[Tooltip("Looping hum AudioSource. Authored on the prefab (loop on, playOnAwake off) — Microwave only Play()/Stop()s it.")]
		[SerializeField] private AudioSource _humSource;
		[Tooltip("One-shot clip played when a cook cycle finishes naturally.")]
		[SerializeField] private AudioClip _plingClip;
		[Range(0f, 1f)] [SerializeField] private float _plingVolume = 1f;

		[Header("Authority")]
		[Tooltip("Host re-validates the requesting player is within this range before applying a button press. 0 = skip the check.")]
		[Min(0f)] [SerializeField] private float _hostValidationRange = 2.5f;

		/// <summary>True while a cook cycle is running. State authority writes; peers render via OnChangedRender.</summary>
		[Networked, OnChangedRender(nameof(OnRunningChanged))]
		public NetworkBool IsRunning { get; private set; }

		/// <summary>True when the door is open. State authority writes; peers render via OnChangedRender.</summary>
		[Networked, OnChangedRender(nameof(OnDoorChanged))]
		public NetworkBool DoorOpen { get; private set; }

		/// <summary>Counts natural cook completions; bumped by the state authority to trigger the synced pling.</summary>
		[Networked, OnChangedRender(nameof(OnFinished))]
		private int FinishCount { get; set; }

		[Networked]
		private TickTimer RunTimer { get; set; }

		// Local mirror of DoorOpen so Update() (a MonoBehaviour callback that runs before
		// Spawned() and on the editor instance) can lerp the door without touching the
		// networked property, which throws until the object is spawned.
		private bool _doorOpenLocal;

		private void Reset()
		{
			_humSource = GetComponentInChildren<AudioSource>();
			if (_humSource != null)
			{
				_humSource.playOnAwake = false;
				_humSource.loop = true;
			}
			_light = GetComponentInChildren<Light>();
		}

		public override void Spawned()
		{
			_doorOpenLocal = DoorOpen;
			ApplyRunningFX();
			SnapDoor();
		}

		// =====================================================================
		// Drive points — wired from the button BaseInteractable.OnInteracted events
		// =====================================================================

		/// <summary>Door button. Toggles the door; sends a state-authority RPC.</summary>
		public void RequestToggleDoor() => RPC_RequestToggleDoor();

		/// <summary>Start button. Begins a cook cycle; sends a state-authority RPC.</summary>
		public void RequestStart() => RPC_RequestStart();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestToggleDoor(RpcInfo info = default)
		{
			if (!ValidateSource(info)) return;

			DoorOpen = !DoorOpen;

			// Opening the door mid-cook cuts the cycle short (no finish pling), like the real thing.
			if (DoorOpen && IsRunning)
			{
				IsRunning = false;
				RunTimer = default;
			}
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestStart(RpcInfo info = default)
		{
			if (!ValidateSource(info)) return;
			if (IsRunning || DoorOpen) return;

			IsRunning = true;
			RunTimer = TickTimer.CreateFromSeconds(Runner, _runSeconds);
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

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;
			if (!IsRunning) return;

			if (RunTimer.Expired(Runner))
			{
				IsRunning = false;
				RunTimer = default;
				FinishCount++; // triggers OnFinished -> pling on every peer
			}
		}

		// =====================================================================
		// Render
		// =====================================================================

		private void Update()
		{
			if (_door == null) return;

			Quaternion target = Quaternion.Euler(_doorOpenLocal ? _doorOpenEuler : _doorClosedEuler);
			_door.localRotation = Quaternion.Slerp(_door.localRotation, target, _doorLerpSpeed * Time.deltaTime);
		}

		private void OnRunningChanged() => ApplyRunningFX();

		private void ApplyRunningFX()
		{
			if (_light != null) _light.enabled = IsRunning;

			if (_humSource != null)
			{
				if (IsRunning) { if (!_humSource.isPlaying) _humSource.Play(); }
				else { if (_humSource.isPlaying) _humSource.Stop(); }
			}
		}

		private void OnDoorChanged() => _doorOpenLocal = DoorOpen; // Update lerps the pose from the local mirror

		private void SnapDoor()
		{
			if (_door == null) return;
			_door.localRotation = Quaternion.Euler(_doorOpenLocal ? _doorOpenEuler : _doorClosedEuler);
		}

		private void OnFinished()
		{
			if (_plingClip != null)
				AudioManager.Instance?.PlaySFX(_plingClip, transform.position, _plingVolume);
		}
	}
}
