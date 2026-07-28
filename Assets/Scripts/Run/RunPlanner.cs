using System.Collections.Generic;
using Starter.City;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>Tuning for how a run is shaped. Lives on <c>RunDirector</c>.</summary>
	[System.Serializable]
	public sealed class RunSettings
	{
		[Header("Objectives")]
		[Tooltip("Terminals that must be activated to win.")]
		[Min(1)] public int ObjectiveCount = 4;

		[Tooltip("Navigation terminals scattered around the city. Using one reveals map area and hands out intel.")]
		public Vector2Int NavTerminalCount = new Vector2Int(3, 5);

		[Tooltip("Minimum metres between two objective buildings. Keeps the four from clustering in one district.")]
		[Min(0f)] public float ObjectiveSeparation = 220f;

		[Tooltip("Minimum metres from the team's start to the nearest objective.")]
		[Min(0f)] public float StartClearance = 180f;

		[Tooltip("Storeys a building needs before it can host an objective. Low buildings make for a dull search.")]
		[Min(1)] public int MinObjectiveFloors = 3;

		[Header("Blocks")]
		[Tooltip("Barriers in series between a site's entrance and its target room (min/max).")]
		public Vector2Int InteriorBarriers = new Vector2Int(1, 2);

		[Tooltip("Distinct keycard tiers in circulation. Fewer tiers means one card opens several readers.")]
		[Min(1)] public int KeycardTiers = 3;

		[Header("Loot / logic slots")]
		[Tooltip("Ordinary buildings opened up as loot and key locations.")]
		[Min(8)] public int StashBuildings = 70;

		[Tooltip("Containers placed per stash building (min/max).")]
		public Vector2Int StashesPerBuilding = new Vector2Int(2, 5);

		[Header("Safety")]
		[Tooltip("Re-rolls allowed before falling back to an all-unlocked plan. Verification failures should be " +
			"rare; this only exists so a pathological seed can never hang or ship an unwinnable run.")]
		[Min(1)] public int MaxAttempts = 12;
	}

	/// <summary>
	/// Builds the run's logic: where the four terminals hide, what blocks the way to each, where every key,
	/// code and clue lives, and where the team starts.
	///
	/// <para><b>Forward fill, not random scatter.</b> Keys are placed one at a time into slots that are
	/// <em>already reachable</em> with the keys placed so far. That single rule is what makes every run solvable
	/// by construction, and it is why the placement loop recomputes reachability on every iteration instead of
	/// shuffling a list once. Scattering keys randomly and hoping produces an unwinnable run often enough to be
	/// unshippable.</para>
	///
	/// <para><b>Multiple ways in.</b> Each site gets several entry routes and only needs one of them open. This is
	/// what the brief's "one run the front doors are shut so climb the outside, another run pick the lock" asks
	/// for, and it is also the pressure valve that keeps a harsh barrier roll from being a dead end.</para>
	///
	/// <para>The result is thrown at <see cref="RunSolver.Verify"/> before it ships. A plan that fails is
	/// re-rolled, never patched.</para>
	/// </summary>
	public static class RunPlanner
	{
		public static RunPlanData Plan(int seed, CityLayout layout, RunSettings settings)
		{
			settings ??= new RunSettings();

			for (int attempt = 0; attempt < settings.MaxAttempts; attempt++)
			{
				// The layout can outlive a plan (static-city mode reuses one layout for every run), and a rejected
				// attempt leaves its BarrierIndex stamps on the shared portals. Wipe them so each attempt — and
				// each run — starts from an ungated city.
				ResetBarrierIndices(layout);

				var plan = TryPlan(seed, attempt, layout, settings);
				if (plan == null) continue;

				if (RunSolver.Verify(plan, out string failure))
				{
					plan.Verified = true;
					if (attempt > 0)
						Debug.Log($"[RunPlanner] Seed {seed} solved on attempt {attempt + 1}.");
					return plan;
				}

				Debug.LogWarning($"[RunPlanner] Seed {seed} attempt {attempt + 1} rejected: {failure}");
			}

			// Every attempt failed. Rather than ship a run nobody can finish, strip the locks: the city and the
			// objectives stay, the puzzle does not. Loud on purpose — this should never happen in practice.
			Debug.LogError($"[RunPlanner] Seed {seed} could not be solved in {settings.MaxAttempts} attempts. " +
				"Falling back to an unlocked plan.");
			ResetBarrierIndices(layout);
			var fallback = TryPlan(seed, 0, layout, settings);
			if (fallback != null)
			{
				for (int i = 0; i < fallback.Barriers.Count; i++)
				{
					fallback.Barriers[i].Kind = BarrierKind.KeyedLock;
					fallback.Barriers[i].Requirement = RunItem.None;
				}
				fallback.Verified = false;
			}
			return fallback;
		}

		private static void ResetBarrierIndices(CityLayout layout)
		{
			for (int i = 0; i < layout.BuildingLots.Count; i++)
			{
				var building = layout.Lots[layout.BuildingLots[i]].Building;
				if (building == null || !building.InteriorGenerated) continue;

				for (int p = 0; p < building.Portals.Count; p++)
				{
					if (building.Portals[p].BarrierIndex < 0) continue;
					var portal = building.Portals[p];
					portal.BarrierIndex = -1;
					building.Portals[p] = portal;
				}
			}
		}

		// =====================================================================

		private static RunPlanData TryPlan(int seed, int attempt, CityLayout layout, RunSettings settings)
		{
			if (layout == null || layout.BuildingLots.Count == 0) return null;

			var plan = new RunPlanData { Seed = seed };
			var rng = RunRng.For(seed, RngStream.Logic, attempt);

			if (!ChooseStart(plan, layout, ref rng)) return null;
			if (!ChooseSites(plan, layout, settings, seed, ref rng)) return null;

			plan.RegionCount = 1 + plan.Sites.Count * 2;

			for (int s = 0; s < plan.Sites.Count; s++)
				BuildSiteBarriers(plan, layout, plan.Sites[s], settings, ref rng);

			BuildStashes(plan, layout, settings, seed, ref rng);
			PlaceBreakers(plan, layout, seed, ref rng);

			if (!PlaceRequiredItems(plan, ref rng)) return null;
			PlaceIntel(plan, ref rng);

			return plan;
		}

		// =====================================================================
		// Start
		// =====================================================================

		/// <summary>
		/// Picks where the team wakes up. Start areas are open ground (a car park, a plaza, a yard) rather than a
		/// building interior, so the opening beat of a run is always "orient yourself in a dark street" — and they
		/// move every run, which is what stops players from memorising a single opening route.
		/// </summary>
		private static bool ChooseStart(RunPlanData plan, CityLayout layout, ref RunRng rng)
		{
			var candidates = new List<int>(64);
			for (int i = 0; i < layout.Lots.Count; i++)
			{
				var lot = layout.Lots[i];
				if (lot.Use is not (LotUse.ParkingLot or LotUse.Plaza or LotUse.Park or LotUse.Yard)) continue;
				if (lot.Area.width < 12f || lot.Area.height < 12f) continue;

				// Outer districts only — starting in the tower core would hand players the densest, most
				// objective-rich part of the map for free.
				var district = layout.DistrictOfLot(lot);
				if (district != null && district.Type == DistrictType.Downtown) continue;

				candidates.Add(i);
			}

			if (candidates.Count == 0)
			{
				// Degenerate layout — fall back to any lot at all so a run can still start.
				for (int i = 0; i < layout.Lots.Count; i++) candidates.Add(i);
				if (candidates.Count == 0) return false;
			}

			int pick = candidates[rng.Index(candidates.Count)];
			var chosen = layout.Lots[pick];
			plan.StartLotIndex = pick;
			plan.StartPosition = chosen.Center;
			plan.StartAddress = layout.AddressOf(chosen);
			return true;
		}

		// =====================================================================
		// Sites
		// =====================================================================

		private static bool ChooseSites(RunPlanData plan, CityLayout layout, RunSettings settings, int seed, ref RunRng rng)
		{
			var pool = new List<int>(layout.BuildingLots);
			rng.Shuffle(pool);

			int navWanted = rng.Range(settings.NavTerminalCount.x, settings.NavTerminalCount.y + 1);

			// Objectives first and with the strictest spacing — they define the shape of the run, and everything
			// else can fit around them.
			if (!PickSites(plan, layout, pool, SiteKind.Objective, settings.ObjectiveCount, settings, seed,
					settings.ObjectiveSeparation, settings.StartClearance, settings.MinObjectiveFloors, ref rng))
				return false;

			// Nav terminals are helpers, so relaxed spacing and no floor requirement — one may sit near the start.
			PickSites(plan, layout, pool, SiteKind.NavTerminal, navWanted, settings, seed,
				settings.ObjectiveSeparation * 0.4f, 40f, 1, ref rng);

			return plan.ObjectiveSites.Count == settings.ObjectiveCount;
		}

		private static bool PickSites(RunPlanData plan, CityLayout layout, List<int> pool, SiteKind kind, int count,
			RunSettings settings, int seed, float separation, float startClearance, int minFloors, ref RunRng rng)
		{
			int placed = 0;

			// Two passes: the first honours every spacing rule, the second drops them. A cramped seed should
			// still produce a playable run rather than no run.
			for (int relax = 0; relax < 2 && placed < count; relax++)
			{
				float sep = relax == 0 ? separation : separation * 0.4f;
				float clear = relax == 0 ? startClearance : startClearance * 0.4f;
				int floorsNeeded = relax == 0 ? minFloors : 1;

				for (int i = 0; i < pool.Count && placed < count; i++)
				{
					int lotIndex = pool[i];
					if (lotIndex < 0) continue;

					var lot = layout.Lots[lotIndex];
					var building = lot.Building;
					if (building == null || building.PlannedFloors < floorsNeeded) continue;

					if (Vector3.Distance(lot.Center, plan.StartPosition) < clear) continue;
					if (TooCloseToExistingSite(plan, layout, lot.Center, sep)) continue;

					var district = layout.DistrictOfLot(lot);
					if (relax == 0 && district != null && !rng.Chance(ObjectivePreference(district.Type, kind)))
						continue;

					BuildingPlanGenerator.EnsureInterior(building, seed);

					var site = new RunSite
					{
						Index = plan.Sites.Count,
						Kind = kind,
						LotIndex = lotIndex,
					};

					if (!ChooseTargetRoom(site, building, kind, ref rng)) continue;

					site.Address = $"{layout.AddressOf(lot)}, {building.FloorLabel(site.TargetLevel)}";
					plan.Sites.Add(site);

					if (kind == SiteKind.Objective) plan.ObjectiveSites.Add(site.Index);
					else if (kind == SiteKind.NavTerminal) plan.NavSites.Add(site.Index);

					pool[i] = -1; // consumed
					placed++;
				}
			}

			return placed == count;
		}

		private static bool TooCloseToExistingSite(RunPlanData plan, CityLayout layout, Vector3 position, float separation)
		{
			for (int i = 0; i < plan.Sites.Count; i++)
			{
				var other = layout.Lots[plan.Sites[i].LotIndex];
				if (Vector3.Distance(other.Center, position) < separation) return true;
			}
			return false;
		}

		/// <summary>Acceptance chance for a district hosting this kind of site. Objectives belong in the kind of
		/// building that plausibly has a secured terminal in it.</summary>
		private static float ObjectivePreference(DistrictType type, SiteKind kind)
		{
			if (kind != SiteKind.Objective) return 0.7f;
			return type switch
			{
				DistrictType.Business    => 0.95f,
				DistrictType.Civic       => 0.9f,
				DistrictType.Downtown    => 0.8f,
				DistrictType.Industrial  => 0.35f,
				DistrictType.Commercial  => 0.3f,
				DistrictType.Residential => 0.15f,
				_                        => 0.4f,
			};
		}

		/// <summary>
		/// Picks the room the terminal actually sits in. Objectives climb: the target level is drawn from the top
		/// third of the building, so "it's in a big office building, top floor" is the common shape and the
		/// vertical journey through the interior is the bulk of the challenge.
		/// </summary>
		private static bool ChooseTargetRoom(RunSite site, BuildingPlan building, SiteKind kind, ref RunRng rng)
		{
			int floors = building.PlannedFloors;
			int level = floors <= 1
				? 0
				: kind == SiteKind.Objective
					? rng.Range(Mathf.Max(1, floors * 2 / 3), floors)
					: rng.Range(0, Mathf.Min(floors, 3));

			var floor = building.Floor(level);
			if (floor == null || floor.Rooms.Count == 0) return false;

			RoomPlan best = null;
			int bestScore = int.MinValue;
			for (int i = 0; i < floor.Rooms.Count; i++)
			{
				var room = floor.Rooms[i];
				if (room.Kind is RoomKind.StairCore or RoomKind.Corridor or RoomKind.Roof or RoomKind.Bathroom) continue;

				int score = room.Kind switch
				{
					RoomKind.ServerRoom => 100,
					RoomKind.Security   => 80,
					RoomKind.Conference => 60,
					RoomKind.Office     => 40,
					RoomKind.Utility    => 20,
					_                   => 10,
				};
				score += Mathf.RoundToInt(room.FloorArea * 0.1f);

				if (score <= bestScore) continue;
				bestScore = score;
				best = room;
			}

			if (best == null) return false;

			site.TargetLevel = level;
			site.TargetRoomIndex = best.Index;
			site.TargetPosition = best.Center;
			return true;
		}

		// =====================================================================
		// Barriers
		// =====================================================================

		private static void BuildSiteBarriers(RunPlanData plan, CityLayout layout, RunSite site,
			RunSettings settings, ref RunRng rng)
		{
			var lot = layout.Lots[site.LotIndex];
			var building = lot.Building;
			if (building == null) return;

			BuildEntryRoutes(plan, site, building, settings, ref rng);
			BuildInteriorBarriers(plan, layout, site, building, settings, ref rng);
		}

		/// <summary>
		/// Composes the ways into a building and blocks each one differently. The variety here is the run-to-run
		/// texture the brief asks for: the same tower is a lockpick problem one night, a climb the next, and a
		/// "find the substation first" problem the night after.
		/// </summary>
		private static void BuildEntryRoutes(RunPlanData plan, RunSite site, BuildingPlan building,
			RunSettings settings, ref RunRng rng)
		{
			// Only exterior portals can be an entry route — an interior door on the ground floor is not a way in.
			int frontDoor = FindExteriorPortal(building, PortalKind.Door, 0, -1);
			int service = FindExteriorPortal(building, PortalKind.Shutter, 0, frontDoor);
			if (service < 0) service = FindExteriorPortal(building, PortalKind.Door, 0, frontDoor);
			int fireEscape = FindExteriorPortal(building, PortalKind.FireEscape, -1, -1);
			int window = FindExteriorPortal(building, PortalKind.Window, -1, -1);
			int roofHatch = FindPortal(building, PortalKind.RoofHatch, -1, -1);

			if (frontDoor >= 0)
				AddRoute(plan, site, building, EntryRouteKind.FrontDoor, frontDoor,
					PickFrontDoorBarrier(ref rng), settings, ref rng);

			if (service >= 0)
				AddRoute(plan, site, building, EntryRouteKind.Service, service,
					rng.Chance(0.5f) ? BarrierKind.Shutter : BarrierKind.Chained, settings, ref rng);

			// Climbing in is always available when the building offers a facade route, and it always costs rope.
			// This is the route that makes "stay off the street" a real strategy rather than flavour text.
			int facadePortal = fireEscape >= 0 ? fireEscape : window;
			if (facadePortal >= 0)
				AddRoute(plan, site, building, EntryRouteKind.Facade, facadePortal, BarrierKind.ClimbOnly, settings, ref rng);

			if (roofHatch >= 0 && building.HasRoofAccess)
				AddRoute(plan, site, building, EntryRouteKind.Rooftop, roofHatch, BarrierKind.ClimbOnly, settings, ref rng);

			EnsureSiteEnterable(plan, site, ref rng);
		}

		private static BarrierKind PickFrontDoorBarrier(ref RunRng rng)
		{
			float roll = rng.NextFloat();
			if (roll < 0.28f) return BarrierKind.KeyedLock;
			if (roll < 0.52f) return BarrierKind.Keypad;
			if (roll < 0.72f) return BarrierKind.CardReader;
			if (roll < 0.87f) return BarrierKind.Chained;
			return BarrierKind.Sealed;
		}

		private static void AddRoute(RunPlanData plan, RunSite site, BuildingPlan building, EntryRouteKind kind,
			int portalIndex, BarrierKind barrierKind, RunSettings settings, ref RunRng rng)
		{
			var route = new EntryRoute { Kind = kind, PortalIndex = portalIndex };

			// Climb routes need the traversal gear that matches how you get up there.
			RunItem requirement = barrierKind == BarrierKind.ClimbOnly
				? new RunItem(kind == EntryRouteKind.Rooftop ? RunItemKind.Planks : RunItemKind.Rope)
				: RequirementFor(barrierKind, site, plan, settings, ref rng);

			int barrier = AddBarrier(plan, site, building, portalIndex, barrierKind, requirement,
				RouteLabel(kind), site.Index, site.Routes.Count, ref rng);
			route.BarrierIndices.Add(barrier);

			site.Routes.Add(route);
		}

		private static string RouteLabel(EntryRouteKind kind) => kind switch
		{
			EntryRouteKind.FrontDoor   => "Main entrance",
			EntryRouteKind.Service     => "Service entrance",
			EntryRouteKind.Facade      => "Facade climb",
			EntryRouteKind.Rooftop     => "Roof access",
			EntryRouteKind.Underground => "Basement access",
			_                          => "Entrance",
		};

		/// <summary>
		/// Guarantees at least one route that is not a hard block. A site whose every entrance is welded shut is
		/// unenterable no matter what the players find, so the harshest roll gets softened here rather than
		/// discovered by the verifier three seconds later.
		/// </summary>
		private static void EnsureSiteEnterable(RunPlanData plan, RunSite site, ref RunRng rng)
		{
			bool anySoft = false;
			for (int r = 0; r < site.Routes.Count && !anySoft; r++)
			{
				var route = site.Routes[r];
				bool allSoft = true;
				for (int b = 0; b < route.BarrierIndices.Count; b++)
				{
					if (!plan.Barriers[route.BarrierIndices[b]].Kind.IsHardBlock()) continue;
					allSoft = false;
					break;
				}
				anySoft = allSoft;
			}
			if (anySoft || site.Routes.Count == 0) return;

			// Downgrade the first hard block we find into a pickable lock.
			var fallback = plan.Barriers[site.Routes[0].BarrierIndices[0]];
			fallback.Kind = BarrierKind.KeyedLock;
			fallback.Requirement = new RunItem(RunItemKind.Key);
		}

		private static void BuildInteriorBarriers(RunPlanData plan, CityLayout layout, RunSite site,
			BuildingPlan building, RunSettings settings, ref RunRng rng)
		{
			int count = rng.Range(settings.InteriorBarriers.x, settings.InteriorBarriers.y + 1);
			if (count <= 0) return;

			var used = new List<int>(count);

			for (int i = 0; i < count; i++)
			{
				int portal = FindInteriorPortal(building, site, used, ref rng);
				if (portal < 0) break;
				used.Add(portal);

				BarrierKind kind = PickInteriorBarrier(ref rng);
				RunItem requirement = kind == BarrierKind.PowerGate
					? new RunItem(RunItemKind.Power, Mathf.Max(0, layout.Lots[site.LotIndex].DistrictIndex))
					: RequirementFor(kind, site, plan, settings, ref rng);

				int index = AddBarrier(plan, site, building, portal, kind, requirement,
					i == count - 1 ? "Secure area" : "Restricted floor", site.Index, -1, ref rng);
				site.InteriorBarriers.Add(index);
			}
		}

		private static BarrierKind PickInteriorBarrier(ref RunRng rng)
		{
			float roll = rng.NextFloat();
			if (roll < 0.32f) return BarrierKind.CardReader;
			if (roll < 0.60f) return BarrierKind.Keypad;
			if (roll < 0.82f) return BarrierKind.PowerGate;
			return BarrierKind.KeyedLock;
		}

		/// <summary>Prefers the door into the target room, then the stairs leading up to its floor — the two places
		/// a block actually changes the route rather than being an inconvenience.</summary>
		private static int FindInteriorPortal(BuildingPlan building, RunSite site, List<int> used, ref RunRng rng)
		{
			for (int i = 0; i < building.Portals.Count; i++)
			{
				var p = building.Portals[i];
				if (used.Contains(i)) continue;
				if (p.LeadsOutside || p.IsVertical) continue;
				if (p.FromLevel != site.TargetLevel) continue;
				if (p.FromRoom != site.TargetRoomIndex && p.ToRoom != site.TargetRoomIndex) continue;
				return i;
			}

			var stairs = new List<int>(8);
			for (int i = 0; i < building.Portals.Count; i++)
			{
				var p = building.Portals[i];
				if (used.Contains(i)) continue;
				if (p.Kind != PortalKind.Stair) continue;
				if (p.ToLevel > site.TargetLevel) continue;
				stairs.Add(i);
			}
			return stairs.Count > 0 ? stairs[rng.Index(stairs.Count)] : -1;
		}

		private static RunItem RequirementFor(BarrierKind kind, RunSite site, RunPlanData plan,
			RunSettings settings, ref RunRng rng)
		{
			// Physical tools are deliberately un-varianted: one crowbar is every crowbar, and a key ring opens any
			// mechanical lock. Only knowledge (pin codes) and keycards come in distinguishable flavours, because
			// those are the ones worth making players hunt for a *specific* instance. Keeping the rest generic
			// means the inventory needs one asset per tool instead of one per lock.
			return kind switch
			{
				BarrierKind.KeyedLock  => new RunItem(RunItemKind.Key),
				BarrierKind.Keypad     => new RunItem(RunItemKind.PinCode, plan.Barriers.Count + 1),
				BarrierKind.CardReader => new RunItem(RunItemKind.Keycard, rng.Range(1, settings.KeycardTiers + 1)),
				BarrierKind.Shutter    => new RunItem(RunItemKind.Crowbar),
				BarrierKind.Chained    => new RunItem(RunItemKind.BoltCutters),
				BarrierKind.ClimbOnly  => new RunItem(RunItemKind.Rope),
				BarrierKind.Sealed     => RunItem.None,
				_                      => RunItem.None,
			};
		}

		private static int AddBarrier(RunPlanData plan, RunSite site, BuildingPlan building, int portalIndex,
			BarrierKind kind, RunItem requirement, string label, int siteIndex, int routeIndex, ref RunRng rng)
		{
			var portal = portalIndex >= 0 && portalIndex < building.Portals.Count
				? building.Portals[portalIndex]
				: default;

			var spec = new BarrierSpec
			{
				Index = plan.Barriers.Count,
				Kind = kind,
				LotIndex = site.LotIndex,
				PortalIndex = portalIndex,
				Requirement = requirement,
				SiteIndex = siteIndex,
				RouteIndex = routeIndex,
				Label = label,
				Position = portal.Position,
				Facing = portal.Facing,
			};

			if (kind == BarrierKind.Keypad)
				spec.Code = $"{rng.Range(0, 10)}{rng.Range(0, 10)}{rng.Range(0, 10)}{rng.Range(0, 10)}";

			plan.Barriers.Add(spec);

			// Point the geometry back at the logic so the realiser can find the barrier from the portal.
			if (portalIndex >= 0 && portalIndex < building.Portals.Count)
			{
				var p = building.Portals[portalIndex];
				p.BarrierIndex = spec.Index;
				building.Portals[portalIndex] = p;
			}

			return spec.Index;
		}

		private static int FindPortal(BuildingPlan building, PortalKind kind, int level, int exclude)
		{
			for (int i = 0; i < building.Portals.Count; i++)
			{
				if (i == exclude) continue;
				var p = building.Portals[i];
				if (p.Kind != kind) continue;
				if (level >= 0 && p.FromLevel != level) continue;
				return i;
			}
			return -1;
		}

		/// <summary>As <see cref="FindPortal"/>, but only portals that connect to the outside world.</summary>
		private static int FindExteriorPortal(BuildingPlan building, PortalKind kind, int level, int exclude)
		{
			for (int i = 0; i < building.Portals.Count; i++)
			{
				if (i == exclude) continue;
				var p = building.Portals[i];
				if (p.Kind != kind || !p.LeadsOutside) continue;
				if (level >= 0 && p.ToLevel != level) continue;
				return i;
			}
			return -1;
		}

		// =====================================================================
		// Slots
		// =====================================================================

		/// <summary>
		/// Opens up ordinary buildings as places to search. These are the run's inventory: every key, code and
		/// clue ends up in one of them. Buildings nearer the start are favoured so the opening minutes have
		/// something to find, but the pool spans the whole city so a run can send players a long way.
		/// </summary>
		private static void BuildStashes(RunPlanData plan, CityLayout layout, RunSettings settings, int seed, ref RunRng rng)
		{
			var pool = new List<int>(layout.BuildingLots);
			for (int s = 0; s < plan.Sites.Count; s++) pool.Remove(plan.Sites[s].LotIndex);
			rng.Shuffle(pool);

			// Bias toward the start without excluding the far side of the city: sort by distance, then take a
			// front-weighted sample.
			pool.Sort((a, b) =>
				Vector3.SqrMagnitude(layout.Lots[a].Center - plan.StartPosition)
					.CompareTo(Vector3.SqrMagnitude(layout.Lots[b].Center - plan.StartPosition)));

			int wanted = Mathf.Min(settings.StashBuildings, pool.Count);
			for (int i = 0; i < wanted; i++)
			{
				// Square-weighted index pull: dense near the start, thinning outward.
				float t = rng.NextFloat();
				int index = Mathf.Clamp(Mathf.FloorToInt(t * t * pool.Count), 0, pool.Count - 1);
				int lotIndex = pool[index];
				pool.RemoveAt(index);

				var building = layout.Lots[lotIndex].Building;
				if (building == null) continue;

				BuildingPlanGenerator.EnsureInterior(building, seed);
				AddStashesInBuilding(plan, lotIndex, building, RunPlanData.StreetRegion, settings, ref rng);
			}

			// Slots inside the gated sites themselves, past the entrance but short of the target. These are what
			// make breaking into one building worthwhile for getting into the next.
			for (int s = 0; s < plan.Sites.Count; s++)
			{
				var site = plan.Sites[s];
				var building = layout.Lots[site.LotIndex].Building;
				if (building == null) continue;

				AddStashesInBuilding(plan, site.LotIndex, building, plan.InteriorRegion(s), settings, ref rng,
					site.TargetLevel, site.TargetRoomIndex);
			}

			AddVendorStock(plan, layout, ref rng);
		}

		private static void AddStashesInBuilding(RunPlanData plan, int lotIndex, BuildingPlan building, int region,
			RunSettings settings, ref RunRng rng, int excludeLevel = -1, int excludeRoom = -1)
		{
			int count = rng.Range(settings.StashesPerBuilding.x, settings.StashesPerBuilding.y + 1);

			for (int i = 0; i < count; i++)
			{
				int level = rng.Index(Mathf.Max(1, building.PlannedFloors));
				var floor = building.Floor(level);
				if (floor == null || floor.Rooms.Count == 0) continue;

				var room = floor.Rooms[rng.Index(floor.Rooms.Count)];
				if (room.Kind is RoomKind.StairCore or RoomKind.Corridor or RoomKind.Roof) continue;
				if (level == excludeLevel && room.Index == excludeRoom) continue;

				// Notes live where people write things down; loot lives everywhere.
				bool note = room.Kind is RoomKind.Office or RoomKind.Security or RoomKind.ServerRoom
					? rng.Chance(0.45f)
					: rng.Chance(0.12f);

				plan.Stashes.Add(new StashSpec
				{
					Index = plan.Stashes.Count,
					Kind = note ? StashKind.Note : StashKind.Container,
					LotIndex = lotIndex,
					Level = level,
					RoomIndex = room.Index,
					Position = JitterInRoom(room, ref rng),
					RegionId = region,
					Contents = RunItem.None,
				});
			}
		}

		/// <summary>Vendor slots sit on open ground near the start — the "buy a clue instead of earning it" path.</summary>
		private static void AddVendorStock(RunPlanData plan, CityLayout layout, ref RunRng rng)
		{
			var candidates = new List<int>(16);
			for (int i = 0; i < layout.Lots.Count; i++)
			{
				var lot = layout.Lots[i];
				if (lot.Use is not (LotUse.Plaza or LotUse.ParkingLot)) continue;
				if (Vector3.Distance(lot.Center, plan.StartPosition) > 320f) continue;
				candidates.Add(i);
			}

			int wanted = Mathf.Min(3, candidates.Count);
			for (int i = 0; i < wanted; i++)
			{
				int pick = candidates[rng.Index(candidates.Count)];
				candidates.Remove(pick);

				plan.Stashes.Add(new StashSpec
				{
					Index = plan.Stashes.Count,
					Kind = StashKind.VendorStock,
					LotIndex = pick,
					Level = 0,
					RoomIndex = -1,
					Position = layout.Lots[pick].Center,
					RegionId = RunPlanData.StreetRegion,
					Contents = RunItem.None,
				});
			}
		}

		/// <summary>
		/// One breaker per district that any barrier demands power from. Breakers sit in ordinary, ungated
		/// buildings — a power gate whose breaker is behind the power gate is the classic randomizer deadlock.
		/// </summary>
		private static void PlaceBreakers(RunPlanData plan, CityLayout layout, int seed, ref RunRng rng)
		{
			var needed = new List<int>(4);
			for (int i = 0; i < plan.Barriers.Count; i++)
			{
				var req = plan.Barriers[i].Requirement;
				if (req.Kind != RunItemKind.Power) continue;
				if (!needed.Contains(req.Variant)) needed.Add(req.Variant);
			}

			for (int n = 0; n < needed.Count; n++)
			{
				int districtIndex = needed[n];
				int lotIndex = FindUtilityLot(plan, layout, districtIndex, seed, ref rng);
				if (lotIndex < 0) continue;

				var building = layout.Lots[lotIndex].Building;
				BuildingPlanGenerator.EnsureInterior(building, seed);

				var room = FindRoom(building, RoomKind.Utility) ?? building.Floor(0)?.Rooms[0];
				if (room == null) continue;

				plan.Stashes.Add(new StashSpec
				{
					Index = plan.Stashes.Count,
					Kind = StashKind.Breaker,
					LotIndex = lotIndex,
					Level = room.Level,
					RoomIndex = room.Index,
					Position = JitterInRoom(room, ref rng),
					RegionId = RunPlanData.StreetRegion,
					Contents = new RunItem(RunItemKind.Power, districtIndex),
					Text = $"Substation feed — {layout.AddressOf(layout.Lots[lotIndex])}",
				});
			}
		}

		private static int FindUtilityLot(RunPlanData plan, CityLayout layout, int districtIndex, int seed, ref RunRng rng)
		{
			var candidates = new List<int>(16);
			for (int i = 0; i < layout.BuildingLots.Count; i++)
			{
				int lotIndex = layout.BuildingLots[i];
				var lot = layout.Lots[lotIndex];
				if (lot.DistrictIndex != districtIndex) continue;
				if (IsSiteLot(plan, lotIndex)) continue;
				candidates.Add(lotIndex);
			}

			if (candidates.Count == 0)
			{
				// District has no free building — fall back to anywhere, so the breaker still exists.
				for (int i = 0; i < layout.BuildingLots.Count; i++)
				{
					int lotIndex = layout.BuildingLots[i];
					if (!IsSiteLot(plan, lotIndex)) candidates.Add(lotIndex);
				}
			}

			return candidates.Count == 0 ? -1 : candidates[rng.Index(candidates.Count)];
		}

		private static bool IsSiteLot(RunPlanData plan, int lotIndex)
		{
			for (int i = 0; i < plan.Sites.Count; i++)
				if (plan.Sites[i].LotIndex == lotIndex) return true;
			return false;
		}

		private static RoomPlan FindRoom(BuildingPlan building, RoomKind kind)
		{
			for (int level = 0; level < building.Floors.Count; level++)
			{
				var room = building.Floors[level].FindRoom(kind);
				if (room != null) return room;
			}
			return null;
		}

		private static Vector3 JitterInRoom(RoomPlan room, ref RunRng rng)
		{
			// Keep clear of the walls so a container never ends up embedded in one.
			float inset = Mathf.Min(1.2f, Mathf.Min(room.Area.width, room.Area.height) * 0.25f);
			return new Vector3(
				rng.Range(room.Area.xMin + inset, room.Area.xMax - inset),
				room.FloorY,
				rng.Range(room.Area.yMin + inset, room.Area.yMax - inset));
		}

		// =====================================================================
		// Forward fill
		// =====================================================================

		/// <summary>
		/// Places every key the run needs, one at a time, into a slot that is <b>already reachable</b> with the
		/// keys placed so far. Reachability is recomputed on every placement, which is exactly what makes the run
		/// solvable by construction: no item can ever end up behind the barrier it opens, or behind a chain that
		/// depends on it.
		/// </summary>
		private static bool PlaceRequiredItems(RunPlanData plan, ref RunRng rng)
		{
			var required = new List<RunItem>(16);
			RunSolver.CollectRequiredItems(plan, required);

			// Power is granted by breakers, which are already placed in always-reachable buildings.
			for (int i = required.Count - 1; i >= 0; i--)
				if (required[i].Kind == RunItemKind.Power) required.RemoveAt(i);

			// The traversal tools are always in circulation, even when no barrier happens to demand them this
			// run — rooftop routes and climbs are core movement, not a puzzle reward.
			AddIfMissing(required, new RunItem(RunItemKind.Rope));
			AddIfMissing(required, new RunItem(RunItemKind.Planks));
			AddIfMissing(required, new RunItem(RunItemKind.Lockpick));
			AddIfMissing(required, new RunItem(RunItemKind.Crowbar));

			rng.Shuffle(required);

			var placed = new HashSet<RunItem>();
			var reachable = new bool[plan.RegionCount];
			var candidates = new List<int>(64);

			while (required.Count > 0)
			{
				RunSolver.ComputeReachable(plan, placed, reachable);

				var item = required[required.Count - 1];
				required.RemoveAt(required.Count - 1);

				// Knowledge is written down, not carried in a crate.
				StashKind preferred = item.IsKnowledge ? StashKind.Note : StashKind.Container;

				if (!CollectFreeSlots(plan, reachable, preferred, candidates) &&
					!CollectFreeSlots(plan, reachable, StashKind.Container, candidates) &&
					!CollectFreeSlots(plan, reachable, StashKind.Note, candidates))
					return false;

				var slot = plan.Stashes[candidates[rng.Index(candidates.Count)]];
				slot.Contents = item;
				placed.Add(item);
			}

			return true;
		}

		private static void AddIfMissing(List<RunItem> list, RunItem item)
		{
			if (!list.Contains(item)) list.Add(item);
		}

		private static bool CollectFreeSlots(RunPlanData plan, bool[] reachable, StashKind kind, List<int> results)
		{
			results.Clear();
			for (int i = 0; i < plan.Stashes.Count; i++)
			{
				var stash = plan.Stashes[i];
				if (stash.Kind != kind) continue;
				if (!stash.Contents.IsNone) continue;
				if (stash.RegionId < 0 || stash.RegionId >= reachable.Length || !reachable[stash.RegionId]) continue;
				results.Add(i);
			}
			return results.Count > 0;
		}

		/// <summary>
		/// Seeds the clue layer. Every objective gets at least one piece of intel somewhere findable, and some
		/// also get a purchasable copy — knowing <em>where</em> is the first problem of every run, and it should
		/// be solvable by exploring, by paying, or by using the city's own navigation network.
		/// </summary>
		private static void PlaceIntel(RunPlanData plan, ref RunRng rng)
		{
			var reachable = new bool[plan.RegionCount];
			var allItems = new HashSet<RunItem>();
			for (int i = 0; i < plan.Stashes.Count; i++)
				if (!plan.Stashes[i].Contents.IsNone) allItems.Add(plan.Stashes[i].Contents);
			RunSolver.ComputeReachable(plan, allItems, reachable);

			var candidates = new List<int>(32);

			for (int ord = 0; ord < plan.ObjectiveSites.Count; ord++)
			{
				var site = plan.Site(plan.ObjectiveSites[ord]);
				var intel = new RunItem(RunItemKind.Intel, ord);
				string text = $"Terminal {(char)('A' + ord)} — {(site != null ? site.Address : "location unknown")}";

				// One findable copy.
				if (CollectFreeSlots(plan, reachable, StashKind.Note, candidates))
				{
					var slot = plan.Stashes[candidates[rng.Index(candidates.Count)]];
					slot.Contents = intel;
					slot.Text = text;
				}

				// Sometimes a second, purchasable copy — the shortcut for a team with money and no time.
				if (rng.Chance(0.6f) && CollectFreeSlots(plan, reachable, StashKind.VendorStock, candidates))
				{
					var slot = plan.Stashes[candidates[rng.Index(candidates.Count)]];
					slot.Contents = intel;
					slot.Text = text;
				}
			}
		}
	}
}
