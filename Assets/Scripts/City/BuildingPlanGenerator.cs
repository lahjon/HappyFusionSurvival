using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Fills in a <see cref="BuildingPlan"/>'s interior: rooms, corridors, stair cores, and the portal graph that
	/// joins everything (including every way in from the outside).
	///
	/// <para><b>Lazy and order-independent.</b> Interiors are generated on demand — by the run planner for the
	/// buildings it wants to hide things in, and by the streamer for whatever players walk up to. Both call
	/// <see cref="EnsureInterior"/>, and because every roll derives from the building's lot index rather than from
	/// a shared running sequence, the two paths produce byte-identical results in either order. That is what lets
	/// a 1&#160;km city with interiors exist at all: at any moment only a few dozen buildings are real.</para>
	///
	/// <para><b>Guillotine partitioning, not free-form BSP.</b> Every region is a rectangle, and every subtraction
	/// of a rectangle from a rectangle yields rectangles. Keeping that invariant means room adjacency is an exact
	/// integer test, doors always land on a real shared wall, and there is no cleanup pass hunting for degenerate
	/// slivers. The cost is that rooms are boxy — which for offices, flats and warehouses is what they should be.</para>
	///
	/// <para><b>Roof is a floor.</b> The roof is generated as level <see cref="BuildingPlan.PlannedFloors"/> with a
	/// single <see cref="RoomKind.Roof"/> room, so rooftop movement is the same graph as everything else and
	/// "cross to the next roof on a plank" is just another portal.</para>
	/// </summary>
	public static class BuildingPlanGenerator
	{
		/// <summary>Smallest room, in module cells, that partitioning will produce. 2&#215;2 cells = 8&#215;8&#160;m.</summary>
		private const int MinRoomCells = 2;

		/// <summary>Chance an adjacency that is not needed for connectivity still gets a door, creating loops.
		/// Loops matter here: a purely tree-shaped floor plan means one blocked door can cut off a whole wing,
		/// which turns a randomizer barrier into a dead end instead of a detour.</summary>
		private const float ExtraDoorChance = 0.35f;

		/// <summary>Generate the interior if it has not been generated yet. Idempotent and thread-agnostic — call
		/// it freely.</summary>
		public static void EnsureInterior(BuildingPlan plan, int seed)
		{
			if (plan == null || plan.InteriorGenerated) return;
			Generate(plan, seed);
		}

		private static void Generate(BuildingPlan plan, int seed)
		{
			plan.InteriorGenerated = true;
			plan.Floors.Clear();
			plan.Portals.Clear();

			var rng = RunRng.For(seed, RngStream.Structure, plan.LotIndex, 977);

			int w = Mathf.Max(2, plan.GridSize.x);
			int h = Mathf.Max(2, plan.GridSize.y);
			var floorRect = new RectInt(0, 0, w, h);

			bool wantsCore = plan.PlannedFloors > 1 || plan.HasRoofAccess;
			RectInt core = wantsCore ? PlaceCore(floorRect, plan.Archetype, ref rng) : new RectInt(0, 0, 0, 0);
			bool hasCore = core.width > 0 && core.height > 0;

			// A corridor spine only makes sense once a floor has enough rooms to need circulation.
			bool corridorLayout = UsesCorridor(plan.Archetype) && Mathf.Min(w, h) >= 4;
			bool corridorHorizontal = w >= h;

			for (int level = 0; level < plan.PlannedFloors; level++)
			{
				var floor = new FloorPlan
				{
					Level = level,
					FloorY = plan.BaseY + level * plan.FloorHeight,
				};
				plan.Floors.Add(floor);

				var levelRng = rng.Fork(level * 131 + 7);
				BuildFloorRooms(plan, floor, floorRect, hasCore ? core : (RectInt?)null,
					corridorLayout, corridorHorizontal, level, ref levelRng);

				ConnectFloor(plan, floor, ref levelRng);
			}

			BuildRoof(plan, floorRect, hasCore ? core : (RectInt?)null);
			BuildVerticalPortals(plan, hasCore ? core : (RectInt?)null);
			BuildExteriorPortals(plan, ref rng);

			IndexPortals(plan);
		}

		// =====================================================================
		// Floor layout
		// =====================================================================

		private static bool UsesCorridor(BuildingArchetype archetype) => archetype switch
		{
			BuildingArchetype.HighRiseOffice => true,
			BuildingArchetype.OfficeBlock    => true,
			BuildingArchetype.ApartmentBlock => true,
			BuildingArchetype.CivicHall      => true,
			_                                => false,
		};

		/// <summary>
		/// Picks the stair core's cell rect. The core is the one thing that must occupy the same footprint on
		/// every floor — it is the building's spine, and both the vertical portal graph and the roof hatch hang
		/// off it. Towers put it dead centre (real cores are structural); smaller buildings tuck it against a
		/// wall so the plan does not lose its whole middle to stairs.
		/// </summary>
		private static RectInt PlaceCore(RectInt floor, BuildingArchetype archetype, ref RunRng rng)
		{
			int cw = Mathf.Min(2, floor.width);
			int ch = Mathf.Min(2, floor.height);

			bool central = archetype is BuildingArchetype.HighRiseOffice or BuildingArchetype.CivicHall;
			if (central && floor.width >= 6 && floor.height >= 6)
			{
				int cx = (floor.width - cw) / 2;
				int cz = (floor.height - ch) / 2;
				return new RectInt(cx, cz, cw, ch);
			}

			// Off-centre but never flush into a corner: a corner core would leave an L-shaped remainder that
			// partitions into awkward ribbons.
			int maxX = Mathf.Max(0, floor.width - cw);
			int maxZ = Mathf.Max(0, floor.height - ch);
			int x = maxX <= 1 ? maxX : rng.Range(0, maxX + 1);
			int z = maxZ <= 1 ? maxZ : rng.Range(0, maxZ + 1);
			return new RectInt(x, z, cw, ch);
		}

		private static void BuildFloorRooms(BuildingPlan plan, FloorPlan floor, RectInt floorRect, RectInt? core,
			bool corridorLayout, bool corridorHorizontal, int level, ref RunRng rng)
		{
			var regions = new List<RectInt>(8);

			if (corridorLayout)
			{
				// Corridor spine through the building, offset so it runs alongside the core rather than through it.
				RectInt corridor = MakeCorridor(floorRect, core, corridorHorizontal);

				// Corridor first: it must exist as a room before anything is carved around it.
				var remaining = new List<RectInt>(4);
				SubtractRect(floorRect, corridor, remaining);

				AddRoom(plan, floor, corridor, RoomKind.Corridor, level);

				for (int i = 0; i < remaining.Count; i++)
				{
					if (core.HasValue && Overlaps(remaining[i], core.Value))
					{
						var pieces = new List<RectInt>(4);
						SubtractRect(remaining[i], core.Value, pieces);
						for (int p = 0; p < pieces.Count; p++) regions.Add(pieces[p]);
					}
					else
					{
						regions.Add(remaining[i]);
					}
				}
			}
			else if (core.HasValue)
			{
				SubtractRect(floorRect, core.Value, regions);
			}
			else
			{
				regions.Add(floorRect);
			}

			if (core.HasValue)
				AddRoom(plan, floor, core.Value, RoomKind.StairCore, level);

			int targetRoomCells = TargetRoomCells(plan.Archetype);
			for (int i = 0; i < regions.Count; i++)
			{
				var leaves = new List<RectInt>(8);
				Partition(regions[i], targetRoomCells, ref rng, leaves, 0);
				for (int l = 0; l < leaves.Count; l++)
					AddRoom(plan, floor, leaves[l], RoomKind.Generic, level);
			}

			AssignRoomKinds(plan, floor, level, ref rng);
		}

		/// <summary>Corridor band, one cell thick, spanning the building and running past the core.</summary>
		private static RectInt MakeCorridor(RectInt floor, RectInt? core, bool horizontal)
		{
			if (horizontal)
			{
				int z = core.HasValue
					? Mathf.Clamp(core.Value.yMin - 1 >= 0 ? core.Value.yMin - 1 : core.Value.yMax, 0, floor.height - 1)
					: floor.height / 2;
				return new RectInt(floor.xMin, z, floor.width, 1);
			}
			else
			{
				int x = core.HasValue
					? Mathf.Clamp(core.Value.xMin - 1 >= 0 ? core.Value.xMin - 1 : core.Value.xMax, 0, floor.width - 1)
					: floor.width / 2;
				return new RectInt(x, floor.yMin, 1, floor.height);
			}
		}

		/// <summary>Target room size in cells per archetype. Bigger numbers mean fewer, larger rooms.</summary>
		private static int TargetRoomCells(BuildingArchetype archetype) => archetype switch
		{
			BuildingArchetype.Warehouse      => 10,
			BuildingArchetype.Supermarket    => 8,
			BuildingArchetype.ParkingGarage  => 64, // effectively "do not subdivide" — a deck is one volume
			BuildingArchetype.HighRiseOffice => 4,
			BuildingArchetype.CivicHall      => 5,
			BuildingArchetype.GasStation     => 16,
			BuildingArchetype.Utility        => 16,
			BuildingArchetype.Shop           => 5,
			_                                => 3,
		};

		/// <summary>Recursive guillotine split until every leaf is near <paramref name="targetCells"/> in area.</summary>
		private static void Partition(RectInt region, int targetCells, ref RunRng rng, List<RectInt> leaves, int depth)
		{
			if (region.width <= 0 || region.height <= 0) return;

			int area = region.width * region.height;
			bool canSplitX = region.width >= MinRoomCells * 2;
			bool canSplitZ = region.height >= MinRoomCells * 2;

			if (depth >= 6 || area <= targetCells || (!canSplitX && !canSplitZ))
			{
				leaves.Add(region);
				return;
			}

			bool splitX = canSplitX && (!canSplitZ || region.width >= region.height);
			if (splitX)
			{
				int cut = rng.Range(MinRoomCells, region.width - MinRoomCells + 1);
				Partition(new RectInt(region.xMin, region.yMin, cut, region.height), targetCells, ref rng, leaves, depth + 1);
				Partition(new RectInt(region.xMin + cut, region.yMin, region.width - cut, region.height), targetCells, ref rng, leaves, depth + 1);
			}
			else
			{
				int cut = rng.Range(MinRoomCells, region.height - MinRoomCells + 1);
				Partition(new RectInt(region.xMin, region.yMin, region.width, cut), targetCells, ref rng, leaves, depth + 1);
				Partition(new RectInt(region.xMin, region.yMin + cut, region.width, region.height - cut), targetCells, ref rng, leaves, depth + 1);
			}
		}

		// =====================================================================
		// Room typing
		// =====================================================================

		/// <summary>
		/// Turns anonymous rectangles into rooms with a purpose. Typing is not cosmetic: the run planner places
		/// objectives in server rooms, power barriers get their breaker from utility rooms, keycards live in
		/// security offices, and loot profiles key off the kind. Every building is therefore guaranteed at least
		/// one utility room, because a building with a power gate and nowhere to restore power is unsolvable.
		/// </summary>
		private static void AssignRoomKinds(BuildingPlan plan, FloorPlan floor, int level, ref RunRng rng)
		{
			bool ground = level == 0;

			// Largest non-core room on the ground floor becomes the lobby / sales floor — it is the room the
			// entrance opens into.
			RoomPlan largest = null;
			for (int i = 0; i < floor.Rooms.Count; i++)
			{
				var r = floor.Rooms[i];
				if (r.Kind is RoomKind.StairCore or RoomKind.Corridor) continue;
				if (largest == null || r.FloorArea > largest.FloorArea) largest = r;
			}

			bool utilityPlaced = false;

			for (int i = 0; i < floor.Rooms.Count; i++)
			{
				var room = floor.Rooms[i];
				if (room.Kind is RoomKind.StairCore or RoomKind.Corridor) continue;

				if (room == largest && ground)
				{
					room.Kind = plan.Archetype switch
					{
						BuildingArchetype.Shop           => RoomKind.Retail,
						BuildingArchetype.Supermarket    => RoomKind.Retail,
						BuildingArchetype.GasStation     => RoomKind.Retail,
						BuildingArchetype.Warehouse      => RoomKind.Storage,
						BuildingArchetype.ParkingGarage  => RoomKind.Deck,
						BuildingArchetype.Utility        => RoomKind.Utility,
						_                                => RoomKind.Lobby,
					};
					if (room.Kind == RoomKind.Utility) utilityPlaced = true;
					continue;
				}

				room.Kind = plan.Archetype switch
				{
					BuildingArchetype.ParkingGarage => RoomKind.Deck,
					BuildingArchetype.Warehouse     => rng.Chance(0.2f) ? RoomKind.Office : RoomKind.Storage,
					BuildingArchetype.Supermarket   => rng.Chance(0.35f) ? RoomKind.Storage : RoomKind.Retail,
					BuildingArchetype.GasStation    => RoomKind.Storage,
					BuildingArchetype.Utility       => RoomKind.Utility,
					BuildingArchetype.Shop          => ground ? RoomKind.Storage : RoomKind.Apartment,
					BuildingArchetype.ApartmentBlock => ground && rng.Chance(0.4f) ? RoomKind.Storage : RoomKind.Apartment,
					_ => PickOfficeRoomKind(ref rng),
				};

				if (room.Kind == RoomKind.Utility) utilityPlaced = true;
			}

			// Guarantee a utility room somewhere in the building. Ground floor is the natural home for the
			// electrical intake, so that is where the fallback goes.
			if (!utilityPlaced && ground)
			{
				RoomPlan smallest = null;
				for (int i = 0; i < floor.Rooms.Count; i++)
				{
					var r = floor.Rooms[i];
					if (r.Kind is RoomKind.StairCore or RoomKind.Corridor or RoomKind.Lobby) continue;
					if (smallest == null || r.FloorArea < smallest.FloorArea) smallest = r;
				}
				if (smallest != null) smallest.Kind = RoomKind.Utility;
			}
		}

		private static RoomKind PickOfficeRoomKind(ref RunRng rng)
		{
			float roll = rng.NextFloat();
			if (roll < 0.55f) return RoomKind.Office;
			if (roll < 0.70f) return RoomKind.Conference;
			if (roll < 0.80f) return RoomKind.Storage;
			if (roll < 0.87f) return RoomKind.Bathroom;
			if (roll < 0.94f) return RoomKind.Utility;
			if (roll < 0.98f) return RoomKind.ServerRoom;
			return RoomKind.Security;
		}

		// =====================================================================
		// Rooms & connectivity
		// =====================================================================

		private static RoomPlan AddRoom(BuildingPlan plan, FloorPlan floor, RectInt cells, RoomKind kind, int level)
		{
			var room = new RoomPlan
			{
				Index = floor.Rooms.Count,
				Level = level,
				Kind = kind,
				Cells = cells,
				Area = CellsToWorld(plan, cells),
				FloorY = plan.BaseY + level * plan.FloorHeight,
				TouchesExterior =
					cells.xMin <= 0 || cells.yMin <= 0 ||
					cells.xMax >= plan.GridSize.x || cells.yMax >= plan.GridSize.y,
			};
			floor.Rooms.Add(room);
			return room;
		}

		/// <summary>
		/// Adds doors until every room on the floor is reachable from every other, then sprinkles extra doors for
		/// loops. Union-find over shuffled adjacencies rather than "door on every shared wall": a floor where
		/// every wall has a door reads as a maze with no structure, and gives the barrier system nothing
		/// meaningful to gate.
		/// </summary>
		private static void ConnectFloor(BuildingPlan plan, FloorPlan floor, ref RunRng rng)
		{
			int n = floor.Rooms.Count;
			if (n <= 1) return;

			var parent = new int[n];
			for (int i = 0; i < n; i++) parent[i] = i;

			var candidates = new List<(int a, int b)>(n * 2);
			for (int a = 0; a < n; a++)
			for (int b = a + 1; b < n; b++)
				if (AreAdjacent(floor.Rooms[a].Cells, floor.Rooms[b].Cells, out _, out _, out _))
					candidates.Add((a, b));

			// Corridors want doors first so they end up as the circulation spine rather than a dead-end closet.
			candidates.Sort((p, q) =>
			{
				int pc = (floor.Rooms[p.a].Kind == RoomKind.Corridor ? 0 : 1) + (floor.Rooms[p.b].Kind == RoomKind.Corridor ? 0 : 1);
				int qc = (floor.Rooms[q.a].Kind == RoomKind.Corridor ? 0 : 1) + (floor.Rooms[q.b].Kind == RoomKind.Corridor ? 0 : 1);
				int cmp = pc.CompareTo(qc);
				if (cmp != 0) return cmp;
				cmp = p.a.CompareTo(q.a);
				return cmp != 0 ? cmp : p.b.CompareTo(q.b);
			});

			for (int i = 0; i < candidates.Count; i++)
			{
				var (a, b) = candidates[i];
				int ra = Find(parent, a), rb = Find(parent, b);

				bool needed = ra != rb;
				if (!needed && !rng.Chance(ExtraDoorChance)) continue;
				if (needed) parent[ra] = rb;

				AddHorizontalPortal(plan, floor, a, b, ref rng);
			}
		}

		private static int Find(int[] parent, int i)
		{
			while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
			return i;
		}

		private static void AddHorizontalPortal(BuildingPlan plan, FloorPlan floor, int a, int b, ref RunRng rng)
		{
			var ra = floor.Rooms[a];
			var rb = floor.Rooms[b];
			if (!AreAdjacent(ra.Cells, rb.Cells, out Vector2Int dir, out int overlapMin, out int overlapMax)) return;

			// Door sits at the middle of the shared wall.
			float mid = (overlapMin + overlapMax) * 0.5f * CityConstants.Module;
			Vector3 pos;
			if (dir.x != 0)
			{
				float x = plan.Footprint.xMin + (dir.x > 0 ? ra.Cells.xMax : ra.Cells.xMin) * CityConstants.Module;
				pos = new Vector3(x, ra.FloorY, plan.Footprint.yMin + mid);
			}
			else
			{
				float z = plan.Footprint.yMin + (dir.y > 0 ? ra.Cells.yMax : ra.Cells.yMin) * CityConstants.Module;
				pos = new Vector3(plan.Footprint.xMin + mid, ra.FloorY, z);
			}

			// Corridor thresholds usually have no door leaf; room-to-room openings do.
			bool openThreshold = (ra.Kind == RoomKind.Corridor || rb.Kind == RoomKind.Corridor) && rng.Chance(0.35f);

			plan.Portals.Add(new RoomPortal
			{
				Kind = openThreshold ? PortalKind.Opening : PortalKind.Door,
				FromRoom = a, FromLevel = ra.Level,
				ToRoom = b, ToLevel = rb.Level,
				Position = pos,
				Facing = new Vector3(dir.x, 0f, dir.y),
				BarrierIndex = -1,
				PortalId = plan.Portals.Count,
			});
		}

		/// <summary>Stairs (and lifts in tall buildings) linking each floor's core, plus the roof hatch.</summary>
		private static void BuildVerticalPortals(BuildingPlan plan, RectInt? core)
		{
			if (!core.HasValue) return;

			bool hasElevator = plan.PlannedFloors >= 6;

			for (int level = 0; level < plan.PlannedFloors; level++)
			{
				var floor = plan.Floor(level);
				var below = floor?.FindRoom(RoomKind.StairCore);
				if (below == null) continue;

				int upperLevel = level + 1;

				// Only buildings with roof access get a stair that continues past the top storey. Without this
				// guard every building would be roof-reachable and the rooftop network would be free.
				if (upperLevel == plan.PlannedFloors && !plan.HasRoofAccess) continue;

				var upperFloor = plan.Floor(upperLevel);
				var above = upperFloor?.FindRoom(RoomKind.StairCore) ?? upperFloor?.FindRoom(RoomKind.Roof);
				if (above == null) continue;

				plan.Portals.Add(new RoomPortal
				{
					Kind = upperFloor.Level == plan.PlannedFloors && plan.HasRoofAccess ? PortalKind.RoofHatch : PortalKind.Stair,
					FromRoom = below.Index, FromLevel = level,
					ToRoom = above.Index, ToLevel = upperLevel,
					Position = below.Center,
					Facing = Vector3.up,
					BarrierIndex = -1,
					PortalId = plan.Portals.Count,
				});

				// The lift is a second, faster vertical route — and one that dies with building power, which is
				// exactly the kind of soft gate the barrier system wants to hang things on.
				if (hasElevator && upperLevel < plan.PlannedFloors)
				{
					plan.Portals.Add(new RoomPortal
					{
						Kind = PortalKind.Elevator,
						FromRoom = below.Index, FromLevel = level,
						ToRoom = above.Index, ToLevel = upperLevel,
						Position = below.Center,
						Facing = Vector3.up,
						BarrierIndex = -1,
						PortalId = plan.Portals.Count,
					});
				}
			}
		}

		/// <summary>Roof as a first-class floor: one open surface at level <see cref="BuildingPlan.PlannedFloors"/>.</summary>
		private static void BuildRoof(BuildingPlan plan, RectInt floorRect, RectInt? core)
		{
			int level = plan.PlannedFloors;
			var roof = new FloorPlan
			{
				Level = level,
				FloorY = plan.BaseY + level * plan.FloorHeight,
			};
			plan.Floors.Add(roof);

			// The bulkhead / hatch keeps the core's footprint so the stair arrives somewhere sensible.
			bool hasBulkhead = core.HasValue && plan.HasRoofAccess;
			if (hasBulkhead)
				AddRoom(plan, roof, core.Value, RoomKind.StairCore, level);

			var surface = AddRoom(plan, roof, floorRect, RoomKind.Roof, level);

			// Without this the stair arrives inside a sealed bulkhead: the vertical portal reaches the roof's
			// core, but nothing connects the core to the roof surface, so the roof would be unreachable in the
			// logic graph even though the geometry is open.
			if (hasBulkhead)
			{
				plan.Portals.Add(new RoomPortal
				{
					Kind = PortalKind.Door,
					FromRoom = 0, FromLevel = level,
					ToRoom = surface.Index, ToLevel = level,
					Position = surface.Center,
					Facing = Vector3.forward,
					BarrierIndex = -1,
					PortalId = plan.Portals.Count,
				});
			}
		}

		// =====================================================================
		// Ways in
		// =====================================================================

		/// <summary>
		/// Every route in from outside: the front door, service entrances, fire escapes and windows. These are the
		/// portals the run planner most often gates — "the front doors are shut this run, so climb" is one barrier
		/// placed on one portal, not a special case anywhere in the building code.
		/// </summary>
		private static void BuildExteriorPortals(BuildingPlan plan, ref RunRng rng)
		{
			var ground = plan.Floor(0);
			if (ground == null) return;

			// --- Front entrance -------------------------------------------------
			var entryRoom = LargestExteriorRoom(ground);
			if (entryRoom != null)
			{
				plan.Portals.Add(MakeExteriorPortal(plan, entryRoom,
					plan.Archetype is BuildingArchetype.Warehouse or BuildingArchetype.Supermarket
						? PortalKind.Shutter
						: PortalKind.Door));
			}

			// --- Service / rear entrance ---------------------------------------
			if (plan.Archetype is BuildingArchetype.Warehouse or BuildingArchetype.Supermarket
				or BuildingArchetype.OfficeBlock or BuildingArchetype.HighRiseOffice or BuildingArchetype.CivicHall)
			{
				var service = SecondExteriorRoom(ground, entryRoom);
				if (service != null)
					plan.Portals.Add(MakeExteriorPortal(plan, service, PortalKind.Door));
			}

			// --- Fire escapes ---------------------------------------------------
			// A fire escape is a full vertical route on the outside of the building: every upper floor gets a
			// window onto it, and it reaches the roof. This is the traversal answer to a locked ground floor.
			for (int s = 0; s < plan.FireEscapeSides.Count; s++)
			{
				int side = plan.FireEscapeSides[s];
				for (int level = 1; level <= plan.PlannedFloors; level++)
				{
					var floor = plan.Floor(level);
					var room = floor != null ? RoomOnSide(floor, plan, side) : null;
					if (room == null) continue;

					plan.Portals.Add(MakeExteriorPortal(plan, room, PortalKind.FireEscape, side));
				}
			}

			// --- Windows ---------------------------------------------------------
			// One window per exterior room on upper floors. Reachable from a ledge, a fire escape, a plank from
			// the neighbouring roof — or breakable, at the cost of noise.
			for (int level = 1; level < plan.PlannedFloors; level++)
			{
				var floor = plan.Floor(level);
				if (floor == null) continue;

				for (int i = 0; i < floor.Rooms.Count; i++)
				{
					var room = floor.Rooms[i];
					if (!room.TouchesExterior || room.Kind is RoomKind.StairCore or RoomKind.Corridor) continue;
					if (!rng.Chance(0.85f)) continue;

					plan.Portals.Add(MakeExteriorPortal(plan, room, PortalKind.Window));
				}
			}

			// --- Ground-floor windows -------------------------------------------
			// Shopfront glass. The loud, fast way into retail when the shutter is down.
			if (plan.Archetype is BuildingArchetype.Shop or BuildingArchetype.Supermarket or BuildingArchetype.GasStation)
			{
				for (int i = 0; i < ground.Rooms.Count; i++)
				{
					var room = ground.Rooms[i];
					if (room.Kind != RoomKind.Retail || !room.TouchesExterior) continue;
					plan.Portals.Add(MakeExteriorPortal(plan, room, PortalKind.Window));
				}
			}
		}

		private static RoomPortal MakeExteriorPortal(BuildingPlan plan, RoomPlan room, PortalKind kind, int side = -1)
		{
			if (side < 0) side = OuterSideOf(room, plan);
			GetSideMidpoint(plan, room, side, out Vector3 pos, out Vector3 facing);

			return new RoomPortal
			{
				Kind = kind,
				FromRoom = -1, FromLevel = room.Level,
				ToRoom = room.Index, ToLevel = room.Level,
				Position = pos,
				Facing = facing,
				BarrierIndex = -1,
				PortalId = plan.Portals.Count,
			};
		}

		private static RoomPlan LargestExteriorRoom(FloorPlan floor)
		{
			RoomPlan best = null;
			for (int i = 0; i < floor.Rooms.Count; i++)
			{
				var r = floor.Rooms[i];
				if (!r.TouchesExterior || r.Kind == RoomKind.StairCore) continue;
				if (best == null || r.FloorArea > best.FloorArea) best = r;
			}
			return best;
		}

		private static RoomPlan SecondExteriorRoom(FloorPlan floor, RoomPlan exclude)
		{
			RoomPlan best = null;
			for (int i = 0; i < floor.Rooms.Count; i++)
			{
				var r = floor.Rooms[i];
				if (r == exclude || !r.TouchesExterior || r.Kind == RoomKind.StairCore) continue;
				if (best == null || r.FloorArea > best.FloorArea) best = r;
			}
			return best;
		}

		private static RoomPlan RoomOnSide(FloorPlan floor, BuildingPlan plan, int side)
		{
			for (int i = 0; i < floor.Rooms.Count; i++)
			{
				var r = floor.Rooms[i];
				if (r.Kind == RoomKind.Roof) return r;
				if (IsOnSide(r, plan, side)) return r;
			}
			return null;
		}

		private static bool IsOnSide(RoomPlan room, BuildingPlan plan, int side) => side switch
		{
			0 => room.Cells.yMin <= 0,
			1 => room.Cells.xMax >= plan.GridSize.x,
			2 => room.Cells.yMax >= plan.GridSize.y,
			_ => room.Cells.xMin <= 0,
		};

		/// <summary>Which exterior side a room touches (first match, in fixed order so it is deterministic).</summary>
		private static int OuterSideOf(RoomPlan room, BuildingPlan plan)
		{
			if (room.Cells.yMin <= 0) return 0;
			if (room.Cells.xMax >= plan.GridSize.x) return 1;
			if (room.Cells.yMax >= plan.GridSize.y) return 2;
			return 3;
		}

		private static void GetSideMidpoint(BuildingPlan plan, RoomPlan room, int side, out Vector3 pos, out Vector3 facing)
		{
			Rect a = room.Area;
			switch (side)
			{
				case 0:  pos = new Vector3(a.center.x, room.FloorY, a.yMin); facing = Vector3.back;    break;
				case 1:  pos = new Vector3(a.xMax, room.FloorY, a.center.y); facing = Vector3.right;   break;
				case 2:  pos = new Vector3(a.center.x, room.FloorY, a.yMax); facing = Vector3.forward; break;
				default: pos = new Vector3(a.xMin, room.FloorY, a.center.y); facing = Vector3.left;    break;
			}
		}

		/// <summary>Back-references so a room can answer "what leads out of me" without scanning every portal.</summary>
		private static void IndexPortals(BuildingPlan plan)
		{
			for (int i = 0; i < plan.Portals.Count; i++)
			{
				var p = plan.Portals[i];
				plan.Room(p.FromLevel, p.FromRoom)?.PortalIndices.Add(i);
				if (p.ToRoom != p.FromRoom || p.ToLevel != p.FromLevel)
					plan.Room(p.ToLevel, p.ToRoom)?.PortalIndices.Add(i);
			}
		}

		// =====================================================================
		// Rect helpers
		// =====================================================================

		/// <summary>
		/// Guillotine subtraction: <paramref name="hole"/> removed from <paramref name="region"/>, leaving up to
		/// four rectangles (left, right, below, above). Preserves the "everything is a rectangle" invariant the
		/// whole generator depends on.
		/// </summary>
		private static void SubtractRect(RectInt region, RectInt hole, List<RectInt> results)
		{
			if (!Overlaps(region, hole))
			{
				results.Add(region);
				return;
			}

			int hx0 = Mathf.Max(region.xMin, hole.xMin);
			int hx1 = Mathf.Min(region.xMax, hole.xMax);
			int hz0 = Mathf.Max(region.yMin, hole.yMin);
			int hz1 = Mathf.Min(region.yMax, hole.yMax);

			if (hx0 > region.xMin)
				results.Add(new RectInt(region.xMin, region.yMin, hx0 - region.xMin, region.height));
			if (hx1 < region.xMax)
				results.Add(new RectInt(hx1, region.yMin, region.xMax - hx1, region.height));
			if (hz0 > region.yMin)
				results.Add(new RectInt(hx0, region.yMin, hx1 - hx0, hz0 - region.yMin));
			if (hz1 < region.yMax)
				results.Add(new RectInt(hx0, hz1, hx1 - hx0, region.yMax - hz1));
		}

		private static bool Overlaps(RectInt a, RectInt b) =>
			a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;

		/// <summary>
		/// True when two cell rects share a wall long enough for a door. <paramref name="dir"/> points from
		/// <paramref name="a"/> toward <paramref name="b"/>; <paramref name="overlapMin"/>/<paramref name="overlapMax"/>
		/// bound the shared wall in cells along the perpendicular axis.
		/// </summary>
		private static bool AreAdjacent(RectInt a, RectInt b, out Vector2Int dir, out int overlapMin, out int overlapMax)
		{
			dir = Vector2Int.zero;
			overlapMin = overlapMax = 0;

			if (a.xMax == b.xMin || b.xMax == a.xMin)
			{
				overlapMin = Mathf.Max(a.yMin, b.yMin);
				overlapMax = Mathf.Min(a.yMax, b.yMax);
				if (overlapMax - overlapMin <= 0) return false;
				dir = new Vector2Int(a.xMax == b.xMin ? 1 : -1, 0);
				return true;
			}

			if (a.yMax == b.yMin || b.yMax == a.yMin)
			{
				overlapMin = Mathf.Max(a.xMin, b.xMin);
				overlapMax = Mathf.Min(a.xMax, b.xMax);
				if (overlapMax - overlapMin <= 0) return false;
				dir = new Vector2Int(0, a.yMax == b.yMin ? 1 : -1);
				return true;
			}

			return false;
		}

		private static Rect CellsToWorld(BuildingPlan plan, RectInt cells) => new Rect(
			plan.Footprint.xMin + cells.xMin * CityConstants.Module,
			plan.Footprint.yMin + cells.yMin * CityConstants.Module,
			cells.width * CityConstants.Module,
			cells.height * CityConstants.Module);
	}
}
