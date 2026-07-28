using System.Collections.Generic;
using Fusion;
using Starter.City;
using Starter.Common;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// Boots and owns a run: generates the city, solves the randomizer, builds the world, spawns the content and
	/// answers "can this player get through that barrier".
	///
	/// <para><b>Seed in, world out.</b> The host picks one integer, writes it to <see cref="RunState.RunSeed"/>,
	/// and every peer generates an identical square kilometre from it — layout, interiors, objective placement,
	/// key placement, the lot. No geometry and no plan data crosses the wire. What does cross the wire is only
	/// what players change: opened barriers, activated terminals, shared knowledge.</para>
	///
	/// <para>Clients deliberately wait for <see cref="RunState.PlanReady"/> before generating. The planner can
	/// reject and re-roll a plan; a client that started generating off the first seed it saw could end up
	/// building a city the host has already thrown away.</para>
	///
	/// <para>Put this on the same scene NetworkObject as <see cref="RunState"/> and <c>GameManager</c>.</para>
	/// </summary>
	[RequireComponent(typeof(RunState))]
	public sealed class RunDirector : NetworkBehaviour
	{
		public static RunDirector Instance { get; private set; }

		/// <summary>Fires on every peer once the city and plan exist locally. UI and AI wait on this.</summary>
		public static event System.Action RunReady;

		[Header("Generation")]
		[Tooltip("City tuning. Leave empty to use built-in defaults.")]
		[SerializeField] private CitySettings _citySettings;

		[Tooltip("Randomizer tuning — objective count, how many blocks, how much loot.")]
		[SerializeField] private RunSettings _runSettings = new RunSettings();

		[Tooltip("Art kit for buildings. Leave empty to build the city from untextured primitives.")]
		[SerializeField] private BuildingKit _kit;

		[Tooltip("Maps the randomizer's logical tools onto real inventory items.")]
		[SerializeField] private RunItemCatalog _catalog;

		[Header("Streaming")]
		[SerializeField] private CityStreamer _streamerPrefab;

		[Header("Content prefabs")]
		[Tooltip("Local (non-networked) barrier object. One prefab covers every barrier kind.")]
		[SerializeField] private Barrier _barrierPrefab;

		[Tooltip("The four win-condition terminals.")]
		[SerializeField] private NetworkObject _objectiveTerminalPrefab;

		[Tooltip("Navigation terminals scattered around the city.")]
		[SerializeField] private NetworkObject _navTerminalPrefab;

		[Tooltip("Ordinary lootable container, used for stash slots with no planned contents.")]
		[SerializeField] private NetworkObject _lootContainerPrefab;

		[Tooltip("World pickup used when a stash slot holds a specific planned item.")]
		[SerializeField] private NetworkObject _pickupPrefab;

		[Tooltip("Readable note / whiteboard that grants knowledge.")]
		[SerializeField] private NetworkObject _notePrefab;

		[Tooltip("Electrical breaker that restores a district's power.")]
		[SerializeField] private NetworkObject _breakerPrefab;

		[Tooltip("Stall that sells intel.")]
		[SerializeField] private NetworkObject _vendorPrefab;

		[Header("Budget")]
		[Tooltip("Networked content objects spawned per tick on the host. Spawning a few hundred objects in one " +
			"tick stalls the simulation, so the host dribbles them in.")]
		[Min(1)] [SerializeField] private int _spawnsPerTick = 12;

		// =====================================================================

		public CityLayout Layout { get; private set; }
		public RunPlanData Plan { get; private set; }
		public RunItemCatalog Catalog => _catalog;
		public CityStreamer Streamer { get; private set; }

		/// <summary>True once this peer has generated the city and plan.</summary>
		public bool IsReady { get; private set; }

		private RunState _state;
		private readonly List<StashSpec> _spawnQueue = new List<StashSpec>(256);
		private int _spawnCursor;
		private bool _contentQueued;
		private readonly List<Barrier> _liveBarriers = new List<Barrier>(32);

		// =====================================================================
		// Lifecycle
		// =====================================================================

		public override void Spawned()
		{
			Instance = this;
			_state = GetComponent<RunState>();
			_catalog?.Bind();

			if (HasStateAuthority)
			{
				// Reuse the project's existing world seed so every other seeded system (loot rolls) agrees with
				// the city about what run this is.
				int seed = WorldGen.Seed != 0 ? WorldGen.Seed : unchecked((int)System.DateTime.UtcNow.Ticks);
				_state.RunSeed = seed;

				Generate(seed);

				_state.ObjectiveTarget = Plan != null ? Plan.ObjectiveSites.Count : 0;
				_state.PlanReady = true;
				QueueContentSpawns();
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
			if (Streamer != null) Streamer.BuildingRealised -= OnBuildingRealised;
		}

		public override void FixedUpdateNetwork()
		{
			// Clients generate as soon as the host says the plan is final.
			if (!IsReady && _state != null && _state.PlanReady && _state.RunSeed != 0)
				Generate(_state.RunSeed);

			if (HasStateAuthority && _contentQueued)
				PumpContentSpawns();
		}

		public override void Render()
		{
			if (!IsReady || Streamer == null) return;
			RefreshObservers();
		}

		// =====================================================================
		// Generation
		// =====================================================================

		private void Generate(int seed)
		{
			if (IsReady) return;

			WorldGen.Seed = seed;

			// Static-city mode: the environment is fixed (seeded by StaticCity.CitySeed, geometry pre-built at
			// scene load, NavMesh baked in the editor) and the run seed randomizes only the content laid on top.
			// Without a StaticCity in the scene, fall back to the fully-procedural streamed city.
			var staticCity = StaticCity.Instance;
			if (staticCity != null) staticCity.EnsureBuilt();

			var timer = System.Diagnostics.Stopwatch.StartNew();
			Layout = staticCity != null ? staticCity.Layout : CityLayoutGenerator.Generate(seed, _citySettings);
			long layoutMs = timer.ElapsedMilliseconds;

			Plan = RunPlanner.Plan(seed, Layout, _runSettings);
			timer.Stop();

			if (Plan != null && Plan.ObjectiveSites.Count > 0)
			{
				// Objective addresses are the single most useful thing in a bug report about a run — log them once.
				for (int i = 0; i < Plan.ObjectiveSites.Count; i++)
				{
					var site = Plan.Site(Plan.ObjectiveSites[i]);
					Debug.Log($"[RunDirector] Objective {(char)('A' + i)}: {site?.Address}");
				}
			}

			Debug.Log($"[RunDirector] Seed {seed}: {Layout.Districts.Count} districts, {Layout.Blocks.Count} blocks, " +
				$"{Layout.BuildingLots.Count} buildings ({layoutMs} ms layout, {timer.ElapsedMilliseconds} ms total). " +
				$"Plan verified: {(Plan != null && Plan.Verified)}");

			if (staticCity != null)
				SpawnAllBarriers();
			else
				CreateStreamer(seed);

			IsReady = true;
			RunReady?.Invoke();
		}

		/// <summary>Static-city mode: every building already exists, so all barriers attach in one pass instead of
		/// trickling in with the streamer.</summary>
		private void SpawnAllBarriers()
		{
			if (Plan == null || _barrierPrefab == null) return;

			var root = new GameObject("Barriers").transform;
			for (int i = 0; i < Plan.Barriers.Count; i++)
				SpawnBarrier(Plan.Barriers[i], root);
		}

		private void CreateStreamer(int seed)
		{
			if (_streamerPrefab != null)
			{
				Streamer = Instantiate(_streamerPrefab);
			}
			else
			{
				// No prefab wired: make one with defaults so a scene with nothing authored still produces a city.
				var go = new GameObject("CityStreamer");
				Streamer = go.AddComponent<CityStreamer>();
			}

			Streamer.name = "City";
			Streamer.Initialize(Layout, _kit, _citySettings, seed);
			Streamer.BuildingRealised += OnBuildingRealised;
		}

		/// <summary>
		/// Keeps the streamer's observer list in sync with the live players. On the host that means every player,
		/// because monsters and hit detection need real colliders wherever anyone is — not just where this peer
		/// happens to be looking.
		/// </summary>
		private void RefreshObservers()
		{
			foreach (var player in Runner.ActivePlayers)
			{
				var obj = Runner.GetPlayerObject(player);
				if (obj != null) Streamer.AddObserver(obj.transform);
			}
		}

		// =====================================================================
		// Barrier realisation
		// =====================================================================

		/// <summary>Attaches this building's barriers as its interior streams in. Barriers are local objects whose
		/// state lives in <see cref="RunState"/>, so they can be created and destroyed freely.</summary>
		private void OnBuildingRealised(CityLot lot, GameObject interior)
		{
			if (Plan == null || _barrierPrefab == null) return;

			for (int i = 0; i < Plan.Barriers.Count; i++)
			{
				var spec = Plan.Barriers[i];
				if (spec.LotIndex != lot.Index) continue;
				SpawnBarrier(spec, interior.transform);
			}

			_liveBarriers.RemoveAll(b => b == null);
		}

		private void SpawnBarrier(BarrierSpec spec, Transform parent)
		{
			// Flatten *before* testing for a usable direction. Vertical portals (stairs, roof hatches) face
			// straight up, so the raw vector is non-zero but its horizontal projection is not — feeding that
			// to LookRotation logs an error and yields an identity rotation anyway.
			Vector3 flatFacing = new Vector3(spec.Facing.x, 0f, spec.Facing.z);
			var rotation = flatFacing.sqrMagnitude > 0.0001f
				? Quaternion.LookRotation(flatFacing.normalized, Vector3.up)
				: Quaternion.identity;

			var barrier = Instantiate(_barrierPrefab, spec.Position, rotation, parent);
			barrier.Configure(spec);
			_liveBarriers.Add(barrier);
		}

		// =====================================================================
		// Requirement checks
		// =====================================================================

		/// <summary>
		/// Local, advisory prediction of whether the player can defeat a barrier — drives the prompt and the
		/// "you need bolt cutters" message. Never trusted for the actual open; that is
		/// <see cref="HostCanOpen"/>.
		/// </summary>
		public bool LocalPlayerSatisfies(BarrierSpec spec)
		{
			if (spec == null) return false;
			if (spec.Kind.IsHardBlock()) return false;
			if (spec.Requirement.IsNone) return true;

			var local = GameManagerLocalPlayerObject();
			return Satisfies(spec, local);
		}

		/// <summary>Host-side authority check. Runs the same rules as the local prediction, but against the
		/// player who actually sent the request.</summary>
		public bool HostCanOpen(BarrierSpec spec, PlayerRef source, Vector3 playerPosition)
		{
			if (spec == null) return false;
			if (spec.Kind.IsHardBlock()) return false;
			if (spec.Requirement.IsNone) return true;

			var playerObject = source == PlayerRef.None ? null : Runner.GetPlayerObject(source);
			return Satisfies(spec, playerObject);
		}

		/// <summary>
		/// The single rule for "is this barrier defeatable by this player right now". Knowledge is checked against
		/// the team's shared record; tools are checked against that player's own inventory — a crowbar three
		/// streets away in someone else's bag does not open this door.
		/// </summary>
		private bool Satisfies(BarrierSpec spec, NetworkObject playerObject)
		{
			var requirement = spec.Requirement;

			if (requirement.IsKnowledge)
				return _state != null && _state.HasKnowledge(requirement);

			if (playerObject == null) return false;

			var inventory = playerObject.GetComponent<Starter.Shooter.Inventory>();
			if (inventory == null) return false;

			if (CarriesItem(inventory, requirement)) return true;

			// Mechanical locks always yield to a lockpick. This is the randomizer's safety valve — see
			// RunSolver.IsOpen, which encodes the same rule so the placer and the game never disagree.
			return spec.Kind.IsPickable() && CarriesItem(inventory, new RunItem(RunItemKind.Lockpick));
		}

		private bool CarriesItem(Starter.Shooter.Inventory inventory, RunItem item)
		{
			if (_catalog == null) return false;
			short id = _catalog.ResolveId(item);
			if (id == 0) return false;
			return InventoryOps.CountItem(inventory.Slots, id) > 0;
		}

		/// <summary>
		/// Host-side consequence of defeating a barrier. Getting through costs noise, and how much depends on how
		/// you did it: a code quietly entered is nothing, a shutter levered open or a chain cut carries streets.
		/// This is the pressure that stops "just crowbar everything" from being the dominant strategy.
		/// </summary>
		public void HostOnBarrierOpened(BarrierSpec spec)
		{
			if (!HasStateAuthority || spec == null) return;

			float loudness = spec.Kind switch
			{
				BarrierKind.Shutter    => 55f,
				BarrierKind.Chained    => 45f,
				BarrierKind.KeyedLock  => 18f,
				BarrierKind.PowerGate  => 25f,
				BarrierKind.CardReader => 8f,
				BarrierKind.Keypad     => 6f,
				_                      => 0f,
			};

			if (loudness > 0f)
				Starter.Monsters.MonsterDirector.ReportNoise(spec.Position, loudness);
		}

		private NetworkObject GameManagerLocalPlayerObject() =>
			Runner != null ? Runner.GetPlayerObject(Runner.LocalPlayer) : null;

		// =====================================================================
		// Content spawning (host only)
		// =====================================================================

		private void QueueContentSpawns()
		{
			if (Plan == null) return;

			_spawnQueue.Clear();
			_spawnQueue.AddRange(Plan.Stashes);
			_spawnCursor = 0;
			_contentQueued = true;

			SpawnSites();
		}

		/// <summary>Objective and navigation terminals. Few enough to spawn in one go.</summary>
		private void SpawnSites()
		{
			for (int i = 0; i < Plan.ObjectiveSites.Count; i++)
			{
				var site = Plan.Site(Plan.ObjectiveSites[i]);
				if (site == null) continue;

				var spawned = SpawnAt(_objectiveTerminalPrefab, site.TargetPosition);
				spawned?.GetComponent<ObjectiveTerminal>()?.HostConfigure(i, site);
			}

			for (int i = 0; i < Plan.NavSites.Count; i++)
			{
				var site = Plan.Site(Plan.NavSites[i]);
				if (site == null) continue;

				var spawned = SpawnAt(_navTerminalPrefab, site.TargetPosition);
				spawned?.GetComponent<NavigationTerminal>()?.HostConfigure(i, site);
			}
		}

		/// <summary>Dribbles the stash objects in a few per tick so a run start never stalls the simulation.</summary>
		private void PumpContentSpawns()
		{
			int budget = _spawnsPerTick;
			while (budget-- > 0 && _spawnCursor < _spawnQueue.Count)
			{
				SpawnStash(_spawnQueue[_spawnCursor]);
				_spawnCursor++;
			}

			if (_spawnCursor >= _spawnQueue.Count)
			{
				_contentQueued = false;
				Debug.Log($"[RunDirector] Spawned {_spawnQueue.Count} content objects.");
			}
		}

		private void SpawnStash(StashSpec stash)
		{
			switch (stash.Kind)
			{
				case StashKind.Note:
				{
					var spawned = SpawnAt(_notePrefab, stash.Position);
					spawned?.GetComponent<ClueNote>()?.HostConfigure(stash);
					break;
				}

				case StashKind.Breaker:
				{
					var spawned = SpawnAt(_breakerPrefab, stash.Position);
					spawned?.GetComponent<PowerBreaker>()?.HostConfigure(stash);
					break;
				}

				case StashKind.VendorStock:
				{
					var spawned = SpawnAt(_vendorPrefab, stash.Position);
					spawned?.GetComponent<IntelVendor>()?.HostConfigure(stash);
					break;
				}

				default:
				{
					if (stash.Contents.IsNone)
					{
						// No planned contents — an ordinary container with its own loot table.
						SpawnAt(_lootContainerPrefab, stash.Position);
					}
					else if (_catalog != null && _pickupPrefab != null)
					{
						short id = _catalog.ResolveId(stash.Contents);
						if (id != 0)
						{
							var spawned = SpawnAt(_pickupPrefab, stash.Position);
							var pickup = spawned != null ? spawned.GetComponent<PickupableItem>() : null;
							if (pickup != null) pickup.ItemId = id;
						}
					}
					break;
				}
			}
		}

		private NetworkObject SpawnAt(NetworkObject prefab, Vector3 position)
		{
			if (prefab == null) return null;
			return Runner.Spawn(prefab, position, Quaternion.identity);
		}

		// =====================================================================
		// Queries used by the rest of the game
		// =====================================================================

		/// <summary>Where the team starts this run. Consumed by <c>GameManager</c>'s spawn path so players land in
		/// the run's start area rather than at an authored scene spawn point.</summary>
		public bool TryGetStartPose(out Vector3 position, out Quaternion rotation)
		{
			position = default;
			rotation = Quaternion.identity;
			if (Plan == null || Layout == null) return false;

			position = Plan.StartPosition + Vector3.up * 0.5f;

			// Face the middle of the city, so the opening view is down a street rather than into a wall.
			Vector3 toCenter = new Vector3(Layout.Bounds.center.x, position.y, Layout.Bounds.center.y) - position;
			if (toCenter.sqrMagnitude > 0.01f)
				rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);

			return true;
		}

		/// <summary>Address of an objective, or null when the team has not learned it yet. This is the whole point
		/// of the intel layer: the terminal exists from the first second, but its location does not.</summary>
		public string KnownObjectiveAddress(int ordinal)
		{
			if (Plan == null || _state == null) return null;
			if (!_state.HasKnowledge(new RunItem(RunItemKind.Intel, ordinal))) return null;
			if (ordinal < 0 || ordinal >= Plan.ObjectiveSites.Count) return null;
			return Plan.Site(Plan.ObjectiveSites[ordinal])?.Address;
		}
	}
}
