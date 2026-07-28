using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>Shared magic numbers for the city. Everything structural is a multiple of <see cref="Module"/>
	/// so kit prefabs snap together and rooms are always describable in whole wall panels.</summary>
	public static class CityConstants
	{
		/// <summary>Side length of one structural module, in metres. Wall panels, floor tiles and room grid cells
		/// are all this size. 4&#160;m matches the Synty modular wall pieces already in the project.</summary>
		public const float Module = 4f;

		/// <summary>Floor-to-floor height. One storey = one wall panel plus slab.</summary>
		public const float FloorHeight = 3.5f;

		/// <summary>Wall thickness used when building interior partitions from primitives.</summary>
		public const float WallThickness = 0.2f;

		/// <summary>Clear width of a door opening.</summary>
		public const float DoorWidth = 1.2f;
		public const float DoorHeight = 2.2f;

		/// <summary>Window openings: sill you mantle over, head you duck under. Sill height is what makes a
		/// window read as a window instead of a floor-level gap, and makes entering one an act of climbing.</summary>
		public const float WindowWidth = 1.6f;
		public const float WindowSillHeight = 1.0f;
		public const float WindowHeadHeight = 2.4f;

		/// <summary>Widest roof-to-roof gap a player can cross with a plank. Gaps beyond this are impassable and
		/// force players back down to street level — which is the entire point of the traversal economy.</summary>
		public const float MaxPlankGap = 7f;

		/// <summary>Gap a player can simply jump, no equipment. Kept short so rooftop routes still cost something.</summary>
		public const float MaxJumpGap = 2.5f;
	}

	/// <summary>
	/// A connection between two rooms, or between a room and the outside world. Portals are the edges of the
	/// building's navigation graph, and they are also the <b>only</b> place a barrier can be attached — which is
	/// what makes the block system composable. The run planner never says "lock this building"; it says "put a
	/// keypad on portal 17", and the same handful of barrier types then work on a front door, a stairwell, a
	/// roof hatch or a shutter without knowing what they are attached to.
	/// </summary>
	public struct RoomPortal
	{
		public PortalKind Kind;

		/// <summary>Room index on <see cref="FromLevel"/>. -1 means "outside the building".</summary>
		public int FromRoom;
		public int FromLevel;

		/// <summary>Room index on <see cref="ToLevel"/>. -1 means "outside the building".</summary>
		public int ToRoom;
		public int ToLevel;

		/// <summary>World-space centre of the opening (at floor level of the lower side).</summary>
		public Vector3 Position;

		/// <summary>Horizontal facing of the opening, pointing from <see cref="FromRoom"/> toward <see cref="ToRoom"/>.</summary>
		public Vector3 Facing;

		/// <summary>Index into <see cref="RunPlanData"/>'s barrier list, or -1 when nothing gates this portal.
		/// Assigned by the run planner after the building is generated, so geometry and logic stay decoupled.</summary>
		public int BarrierIndex;

		/// <summary>Stable identity for this portal within its building, used to key networked door state without
		/// spawning a NetworkObject per door.</summary>
		public int PortalId;

		public bool LeadsOutside => FromRoom < 0 || ToRoom < 0;
		public bool IsVertical => FromLevel != ToLevel;
	}

	/// <summary>One room on one floor. Rooms are axis-aligned rectangles on the building's module grid.</summary>
	public sealed class RoomPlan
	{
		public int Index;
		public int Level;
		public RoomKind Kind;

		/// <summary>Room bounds in module-grid cells, local to the building footprint.</summary>
		public RectInt Cells;

		/// <summary>Room bounds in world space (XZ).</summary>
		public Rect Area;

		/// <summary>Floor surface height in world space.</summary>
		public float FloorY;

		/// <summary>True when at least one of the room's walls is on the building exterior — i.e. it can have
		/// windows, and can be entered from a ledge or fire escape.</summary>
		public bool TouchesExterior;

		/// <summary>Portal indices (into the owning <see cref="FloorPlan.Portals"/> plus vertical portals) that
		/// touch this room. Filled once the portal set is final.</summary>
		public readonly List<int> PortalIndices = new List<int>(4);

		public Vector3 Center => new Vector3(Area.center.x, FloorY, Area.center.y);
		public float FloorArea => Area.width * Area.height;
	}

	/// <summary>One storey of a building.</summary>
	public sealed class FloorPlan
	{
		public int Level;
		public float FloorY;
		public readonly List<RoomPlan> Rooms = new List<RoomPlan>(12);

		public RoomPlan RoomAt(int index) => index >= 0 && index < Rooms.Count ? Rooms[index] : null;

		/// <summary>First room of the given kind, or null.</summary>
		public RoomPlan FindRoom(RoomKind kind)
		{
			for (int i = 0; i < Rooms.Count; i++)
				if (Rooms[i].Kind == kind) return Rooms[i];
			return null;
		}
	}

	/// <summary>
	/// The complete logical description of one building: footprint, storeys, rooms, and the portal graph that
	/// joins them (including the ways in from outside). Generated on demand by
	/// <see cref="BuildingPlanGenerator"/> and cached on the lot — geometry is only realised when a player gets
	/// close, but the plan exists from match start because the run planner needs to reason about every
	/// building's interior before anyone has moved.
	/// </summary>
	public sealed class BuildingPlan
	{
		public int LotIndex;
		public string Name;
		public BuildingArchetype Archetype;

		/// <summary>Building footprint in world space (XZ), already inset from the lot by its setback.</summary>
		public Rect Footprint;

		/// <summary>Footprint size in module cells.</summary>
		public Vector2Int GridSize;

		/// <summary>Ground level Y for this building.</summary>
		public float BaseY;

		public float FloorHeight = CityConstants.FloorHeight;

		/// <summary>
		/// Storey count, decided when the shell is roughed out. This is the authoritative height: it is known for
		/// every building in the city from match start, whereas <see cref="Floors"/> is only populated once the
		/// interior is actually needed.
		/// </summary>
		public int PlannedFloors = 1;

		/// <summary>
		/// True once <see cref="BuildingPlanGenerator"/> has partitioned the interior. Interiors are generated
		/// lazily — the run planner forces the handful of buildings it cares about, and the streamer forces
		/// whatever the players walk up to. Because generation is seeded off the building's lot index rather than
		/// call order, both paths produce identical rooms no matter which runs first.
		/// </summary>
		public bool InteriorGenerated;

		public readonly List<FloorPlan> Floors = new List<FloorPlan>(4);

		/// <summary>Every portal in the building — horizontal (within a floor), vertical (stairs/lifts), and
		/// exterior (doors, windows, fire escapes, roof hatch). One flat list so the planner can index it.</summary>
		public readonly List<RoomPortal> Portals = new List<RoomPortal>(32);

		/// <summary>True when the roof is a usable surface with a hatch or stair bulkhead. Roof-accessible
		/// buildings are the backbone of the rooftop route network.</summary>
		public bool HasRoofAccess;

		/// <summary>Facade sides (0=-Z, 1=+X, 2=+Z, 3=-X) carrying a climbable fire escape.</summary>
		public readonly List<int> FireEscapeSides = new List<int>(2);

		/// <summary>Height of the parapet wall around a flat roof. Cover from sightlines, and the thing you
		/// vault when crossing a plank.</summary>
		public float ParapetHeight = 1.1f;

		public int FloorCount => PlannedFloors;

		/// <summary>Y of the roof surface.</summary>
		public float RoofY => BaseY + FloorCount * FloorHeight;

		public Vector3 Center => new Vector3(Footprint.center.x, BaseY, Footprint.center.y);

		public FloorPlan Floor(int level) => level >= 0 && level < Floors.Count ? Floors[level] : null;

		public RoomPlan Room(int level, int index) => Floor(level)?.RoomAt(index);

		/// <summary>Total room count across every floor — a cheap proxy for "how much loot / how many hiding
		/// places does this building hold".</summary>
		public int TotalRooms
		{
			get
			{
				int n = 0;
				for (int i = 0; i < Floors.Count; i++) n += Floors[i].Rooms.Count;
				return n;
			}
		}

		/// <summary>Human-readable floor label for clues: "ground floor", "3rd floor", "top floor".</summary>
		public string FloorLabel(int level)
		{
			if (level <= 0) return "the ground floor";
			if (level == FloorCount - 1) return "the top floor";
			int n = level + 1;
			string suffix = (n % 100 is >= 11 and <= 13) ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
			return $"the {n}{suffix} floor";
		}
	}
}
