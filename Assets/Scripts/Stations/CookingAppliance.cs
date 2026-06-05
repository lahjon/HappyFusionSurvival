using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Abstract <see cref="OpenableAppliance"/> that adds a timed cook/heat cycle — the shared core of
	/// the <see cref="Microwave"/> and <see cref="Oven"/>. Layered on the base's networked door:
	///
	/// - <see cref="IsCooking"/> / <see cref="CookTimer"/>: a <c>[Networked]</c> bool + <c>TickTimer</c>
	///   the state authority watches in <see cref="FixedUpdateNetwork"/>. A looping <see cref="_humSource"/>
	///   plays and the interior light stays on while cooking; on natural completion a one-shot
	///   "ding" fires via a <c>[Networked]</c> counter + <c>OnChangedRender</c> (tolerates lost ticks).
	/// - <b>Safety interlock</b>: can't start while the door is open, and opening the door mid-cook cuts
	///   the cycle short with no completion ding — exactly like the real things.
	///
	/// Trigger the cook from a button child wired to <see cref="RequestToggleCook"/> (a generic
	/// <c>BaseInteractable</c> → UnityEvent works; no bespoke button type needed).
	/// </summary>
	public abstract class CookingAppliance : OpenableAppliance
	{
		[Header("Cook Cycle")]
		[Tooltip("How long one cook/heat cycle runs, in seconds.")]
		[Min(0.1f)] [SerializeField] private float _cookSeconds = 10f;
		[Tooltip("Looping hum/heat AudioSource. Author on the prefab (loop on, playOnAwake off) — this only Play()/Stop()s it.")]
		[SerializeField] private AudioSource _humSource;
		[Tooltip("One-shot clip played when a cycle finishes naturally (the 'ding').")]
		[SerializeField] private AudioClip _finishClip;
		[Range(0f, 1f)] [SerializeField] private float _finishVolume = 1f;
		[Tooltip("Optional objects enabled only while cooking — heating coils, a glow, the turntable, etc.")]
		[SerializeField] private GameObject[] _activeWhileCooking;
		[Tooltip("ON (oven): can start/keep heating with the door open. OFF (microwave): door must be shut to " +
		         "start, and opening it mid-cycle aborts the cook (the safety cut-off).")]
		[SerializeField] private bool _cookWithDoorOpen = false;

		/// <summary>True while a cook cycle is running. State authority writes; peers render via OnChangedRender.</summary>
		[Networked, OnChangedRender(nameof(OnCookingRender))]
		public NetworkBool IsCooking { get; private set; }

		/// <summary>Counts natural completions; bumped by the state authority to trigger the synced finish ding.</summary>
		[Networked, OnChangedRender(nameof(OnFinished))]
		private int FinishCount { get; set; }

		[Networked] private TickTimer CookTimer { get; set; }

		protected override void Reset()
		{
			base.Reset();
			_humSource = GetComponentInChildren<AudioSource>();
			if (_humSource != null)
			{
				_humSource.playOnAwake = false;
				_humSource.loop = true;
			}
		}

		public override void Spawned()
		{
			base.Spawned();
			ApplyCookFX();
		}

		// ── Drive point ─────────────────────────────────────────────────────────────
		/// <summary>Start a cycle, or stop one already running. Public so a button child can drive it; sends a state-authority RPC.</summary>
		public void RequestToggleCook() => RPC_RequestToggleCook();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestToggleCook(RpcInfo info = default)
		{
			if (!ValidateSource(info)) return;

			// Pressing the control while running stops the cycle short (no finish ding), like hitting Stop.
			if (IsCooking)
			{
				StopCook();
				return;
			}

			if (IsOpen && !_cookWithDoorOpen) return; // microwave safety interlock — won't run with the door open
			IsCooking = true;
			CookTimer = TickTimer.CreateFromSeconds(Runner, _cookSeconds);
		}

		// Opening the door mid-cook cuts the cycle short (no finish ding), like the real safety cut-off.
		protected override void OnOpenToggledAuthority()
		{
			if (IsOpen && IsCooking && !_cookWithDoorOpen) StopCook();
		}

		private void StopCook()
		{
			IsCooking = false;
			CookTimer = default;
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false || !IsCooking) return;

			if (CookTimer.Expired(Runner))
			{
				IsCooking = false;
				CookTimer = default;
				FinishCount++; // triggers OnFinished -> ding on every peer
			}
		}

		// ── Render ───────────────────────────────────────────────────────────────
		// Interior light is on while open OR cooking (a microwave/oven lights up when you open it AND while it runs).
		protected override bool LightShouldBeOn => base.LightShouldBeOn || IsCooking;

		private void OnCookingRender()
		{
			ApplyCookFX();
			ApplyLight(); // cooking state feeds LightShouldBeOn
		}

		private void ApplyCookFX()
		{
			if (_humSource != null)
			{
				if (IsCooking) { if (!_humSource.isPlaying) _humSource.Play(); }
				else if (_humSource.isPlaying) _humSource.Stop();
			}

			if (_activeWhileCooking != null)
			{
				for (int i = 0; i < _activeWhileCooking.Length; i++)
					if (_activeWhileCooking[i] != null && _activeWhileCooking[i].activeSelf != (bool)IsCooking)
						_activeWhileCooking[i].SetActive(IsCooking);
			}
		}

		private void OnFinished()
		{
			if (_finishClip != null)
				AudioManager.Instance?.PlaySFX(_finishClip, InteractPoint, _finishVolume);
		}
	}
}
