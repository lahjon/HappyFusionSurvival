using System;
using Fusion;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// The run's mutable progress, and the only thing about a run that is networked.
	///
	/// <para>Everything else — the city, the floorplans, where the terminals are, what each barrier wants — is
	/// derived from the seed and computed identically on every peer, so none of it needs replicating. What cannot
	/// be derived is what the <em>players have done</em>: which barriers they have opened, which terminals are
	/// live, what the team has learned. That fits in a few hundred bytes and lives here.</para>
	///
	/// <para>Deliberately a scene singleton with flat arrays rather than a NetworkObject per barrier. A run has
	/// tens of barriers spread over a square kilometre; spawning a networked object for each would cost far more
	/// than it buys, and this project has repeatedly been bitten by NetworkBehaviour prefab bakes going stale.
	/// One object, one bake.</para>
	/// </summary>
	public sealed class RunState : NetworkBehaviour
	{
		public static RunState Instance { get; private set; }

		/// <summary>Fires on every peer when any barrier's open state changes. Barriers listen and update visuals.</summary>
		public static event Action BarriersChanged;

		/// <summary>Fires on every peer when the team learns something (a code, a location, network access).</summary>
		public static event Action<RunItem> KnowledgeGained;

		/// <summary>Fires on every peer when an objective terminal comes online. Argument is the objective ordinal.</summary>
		public static event Action<int> ObjectiveActivated;

		/// <summary>Fires on every peer when the last terminal goes live and the run is won.</summary>
		public static event Action RunCompleted;

		public const int MaxBarriers = 128;
		public const int MaxObjectives = 8;
		public const int MaxKnowledge = 64;

		// =========================================================================
		// Networked state
		// =========================================================================

		/// <summary>Seed the whole run is generated from. The host picks it; clients read it here and build an
		/// identical city and plan locally.</summary>
		[Networked] public int RunSeed { get; set; }

		/// <summary>Set by the host once it has generated and verified the plan. Clients wait for this before
		/// generating, so a client can never build against a seed the host later rerolled.</summary>
		[Networked] public NetworkBool PlanReady { get; set; }

		/// <summary>1 = open, 0 = still blocked. Indexed by <see cref="BarrierSpec.Index"/>.</summary>
		[Networked, Capacity(MaxBarriers)]
		private NetworkArray<byte> BarrierOpen { get; }

		/// <summary>Bumped whenever <see cref="BarrierOpen"/> changes so peers react with one callback instead of
		/// diffing an array every frame.</summary>
		[Networked, OnChangedRender(nameof(OnBarrierVersionChanged))]
		private int BarrierVersion { get; set; }

		/// <summary>1 = activated. Indexed by objective ordinal (0..3).</summary>
		[Networked, Capacity(MaxObjectives)]
		private NetworkArray<byte> ObjectiveActive { get; }

		[Networked, OnChangedRender(nameof(OnObjectiveVersionChanged))]
		private int ObjectiveVersion { get; set; }

		/// <summary>
		/// Packed <see cref="RunItem"/>s the team has learned. Knowledge is shared the instant one player finds
		/// it: four players should not each have to walk to the same sticky note to read the same four digits,
		/// and shouting a code across voice chat would make any other design a formality anyway.
		/// </summary>
		[Networked, Capacity(MaxKnowledge)]
		private NetworkArray<int> Knowledge { get; }

		[Networked] private int KnowledgeCount { get; set; }

		[Networked, OnChangedRender(nameof(OnKnowledgeVersionChanged))]
		private int KnowledgeVersion { get; set; }

		/// <summary>How many terminals are live. Drives monster escalation and the win check.</summary>
		[Networked] public int ActivatedObjectives { get; private set; }

		/// <summary>True once every objective is active. The run is over.</summary>
		[Networked, OnChangedRender(nameof(OnRunCompleteChanged))]
		public NetworkBool RunComplete { get; private set; }

		/// <summary>Objectives needed to win. Host writes it from the plan so clients can render "2 / 4".</summary>
		[Networked] public int ObjectiveTarget { get; set; }

		// =========================================================================
		// Lifecycle
		// =========================================================================

		/// <summary>
		/// Local mirror of <see cref="ObjectiveActive"/>, used to fire <see cref="ObjectiveActivated"/> only on a
		/// genuine 0→1 transition. Without it the version callback re-announces every already-live objective on
		/// every change, and subscribers that <em>do</em> something on activation — the monster director spawns
		/// reinforcements — compound with each subsequent terminal.
		/// </summary>
		private readonly bool[] _announcedObjectives = new bool[MaxObjectives];

		public override void Spawned()
		{
			Instance = this;

			// Seed the mirror from current state rather than replaying it. A late joiner must not fire activation
			// events for objectives that went live before it connected; components that need the current value
			// read it directly in their own Spawned (see ObjectiveTerminal).
			for (int i = 0; i < MaxObjectives; i++)
				_announcedObjectives[i] = ObjectiveActive[i] != 0;

			// Barriers are idempotent — they re-read the array and set a bool — so replaying them is safe and
			// lets a late joiner's streamed-in barriers start in the right state.
			BarriersChanged?.Invoke();
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		// =========================================================================
		// Reads (every peer)
		// =========================================================================

		public bool IsBarrierOpen(int barrierIndex) =>
			barrierIndex >= 0 && barrierIndex < MaxBarriers && BarrierOpen[barrierIndex] != 0;

		public bool IsObjectiveActive(int ordinal) =>
			ordinal >= 0 && ordinal < MaxObjectives && ObjectiveActive[ordinal] != 0;

		/// <summary>True when the team has learned <paramref name="item"/>.</summary>
		public bool HasKnowledge(RunItem item)
		{
			int packed = Pack(item);
			int count = Mathf.Min(KnowledgeCount, MaxKnowledge);
			for (int i = 0; i < count; i++)
				if (Knowledge[i] == packed) return true;
			return false;
		}

		/// <summary>Enumerate the team's knowledge — used by the objective HUD and the map overlay.</summary>
		public RunItem KnowledgeAt(int index) =>
			index >= 0 && index < Mathf.Min(KnowledgeCount, MaxKnowledge) ? Unpack(Knowledge[index]) : RunItem.None;

		public int KnownCount => Mathf.Min(KnowledgeCount, MaxKnowledge);

		// =========================================================================
		// Authority writes
		// =========================================================================

		/// <summary>
		/// Host-side barrier open. Every path that defeats a block funnels through here — key, code, crowbar,
		/// power — so there is exactly one place that decides a barrier is down and exactly one place that has to
		/// be right.
		/// </summary>
		public void AuthorityOpenBarrier(int barrierIndex)
		{
			if (HasStateAuthority == false) return;
			if (barrierIndex < 0 || barrierIndex >= MaxBarriers) return;
			if (BarrierOpen[barrierIndex] != 0) return;

			BarrierOpen.Set(barrierIndex, 1);
			BarrierVersion++;
		}

		/// <summary>Host-side: record something the team now knows. Ignores duplicates and silently drops overflow
		/// rather than corrupting the array.</summary>
		public void AuthorityGrantKnowledge(RunItem item)
		{
			if (HasStateAuthority == false) return;
			if (!item.IsKnowledge || item.IsNone) return;
			if (HasKnowledge(item)) return;
			if (KnowledgeCount >= MaxKnowledge)
			{
				Debug.LogWarning("[RunState] Knowledge array full — dropping " + item);
				return;
			}

			Knowledge.Set(KnowledgeCount, Pack(item));
			KnowledgeCount++;
			KnowledgeVersion++;
		}

		/// <summary>Host-side: bring an objective terminal online and check for the win.</summary>
		public void AuthorityActivateObjective(int ordinal)
		{
			if (HasStateAuthority == false) return;
			if (ordinal < 0 || ordinal >= MaxObjectives) return;
			if (ObjectiveActive[ordinal] != 0) return;

			ObjectiveActive.Set(ordinal, 1);
			ActivatedObjectives++;
			ObjectiveVersion++;

			if (ObjectiveTarget > 0 && ActivatedObjectives >= ObjectiveTarget)
				RunComplete = true;
		}

		// =========================================================================
		// RPCs (client request → host validates)
		// =========================================================================

		/// <summary>
		/// Client asks to open a barrier. The host re-checks proximity and the requirement itself — a client
		/// saying "I opened it" is a request, never a fact.
		/// </summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestOpenBarrier(int barrierIndex, Vector3 playerPosition, RpcInfo info = default)
		{
			if (HasStateAuthority == false) return;

			var director = RunDirector.Instance;
			var spec = director != null ? director.Plan?.Barrier(barrierIndex) : null;
			if (spec == null) return;

			// Position is re-validated host-side with the usual interaction slack.
			if (Vector3.Distance(playerPosition, spec.Position) > Barrier.InteractRangeMeters * 1.5f) return;

			if (!director.HostCanOpen(spec, info.Source, playerPosition)) return;

			AuthorityOpenBarrier(barrierIndex);
			director.HostOnBarrierOpened(spec);
		}

		/// <summary>Client reports learning something (read a note, bought intel, used a nav terminal).</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_ReportKnowledge(byte kind, int variant)
		{
			if (HasStateAuthority == false) return;
			AuthorityGrantKnowledge(new RunItem((RunItemKind)kind, variant));
		}

		/// <summary>Client asks to activate an objective terminal it is standing at.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestActivateObjective(int ordinal, Vector3 playerPosition)
		{
			if (HasStateAuthority == false) return;

			var director = RunDirector.Instance;
			var plan = director != null ? director.Plan : null;
			if (plan == null || ordinal < 0 || ordinal >= plan.ObjectiveSites.Count) return;

			var site = plan.Site(plan.ObjectiveSites[ordinal]);
			if (site == null) return;
			if (Vector3.Distance(playerPosition, site.TargetPosition) > 6f) return;

			AuthorityActivateObjective(ordinal);
		}

		// =========================================================================
		// Change callbacks
		// =========================================================================

		private void OnBarrierVersionChanged() => BarriersChanged?.Invoke();

		private void OnObjectiveVersionChanged()
		{
			for (int i = 0; i < MaxObjectives; i++)
			{
				bool active = ObjectiveActive[i] != 0;
				if (active == _announcedObjectives[i]) continue;

				_announcedObjectives[i] = active;
				if (active) ObjectiveActivated?.Invoke(i);
			}
		}

		private void OnKnowledgeVersionChanged()
		{
			int count = Mathf.Min(KnowledgeCount, MaxKnowledge);
			if (count > 0) KnowledgeGained?.Invoke(Unpack(Knowledge[count - 1]));
		}

		private void OnRunCompleteChanged()
		{
			if (RunComplete) RunCompleted?.Invoke();
		}

		// =========================================================================

		private static int Pack(RunItem item) => ((int)item.Kind << 24) | (item.Variant & 0x00FFFFFF);

		private static RunItem Unpack(int packed) =>
			new RunItem((RunItemKind)((packed >> 24) & 0xFF), packed & 0x00FFFFFF);
	}
}
