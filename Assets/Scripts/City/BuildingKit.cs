using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Art binding for the city generator: which prefabs dress a given kind of building, and which materials the
	/// procedural fallback uses.
	///
	/// <para><b>The generator never references art directly.</b> It emits "there is an exterior wall panel here,
	/// facing this way, and it has a window", and the kit decides what that looks like. That indirection is what
	/// lets the same city run on grey boxes today and on a Synty modular kit tomorrow, and lets one district use a
	/// different kit from another without a second generator.</para>
	///
	/// <para>Every prefab slot is optional. Whatever is left empty is built from primitives with the materials
	/// below, so generation is never blocked on art existing — a missing wall prefab costs you fidelity, not a
	/// hole in the building.</para>
	/// </summary>
	[CreateAssetMenu(menuName = "HorrorTown/Building Kit", fileName = "BuildingKit")]
	public sealed class BuildingKit : ScriptableObject
	{
		[Header("Identity")]
		[Tooltip("Archetypes this kit can dress. Empty means it is the fallback for everything.")]
		public BuildingArchetype[] Archetypes;

		[Header("Exterior modules (one module = 4 m)")]
		[Tooltip("Blank exterior wall panel. Pivot at the panel's bottom-centre, facing +Z.")]
		public GameObject ExteriorWall;
		public GameObject ExteriorWallWindow;
		public GameObject ExteriorWallDoor;
		public GameObject ExteriorWallShutter;
		[Tooltip("Ground-floor glazing for retail frontage.")]
		public GameObject ShopFront;
		public GameObject Corner;

		[Header("Interior modules")]
		public GameObject InteriorWall;
		public GameObject InteriorWallDoor;
		public GameObject FloorTile;
		public GameObject CeilingTile;
		[Tooltip("One storey of stairs. Pivot at the bottom step, ascending +Z.")]
		public GameObject Stairs;

		[Header("Roof")]
		public GameObject RoofTile;
		[Tooltip("Parapet wall section around a flat roof.")]
		public GameObject Parapet;
		[Tooltip("Bulkhead / hatch housing where the stair core meets the roof.")]
		public GameObject RoofBulkhead;

		[Header("Facade features")]
		[Tooltip("One storey of exterior fire escape. Pivot at the bottom, ladder facing +Z.")]
		public GameObject FireEscape;
		public GameObject Drainpipe;
		[Tooltip("Ledge / cornice used as a climbing hold between storeys.")]
		public GameObject Ledge;

		[Header("Procedural fallback materials")]
		[Tooltip("Used for building massing and walls when no prefab is assigned.")]
		public Material WallMaterial;
		public Material FloorMaterial;
		public Material RoofMaterial;
		public Material RoadMaterial;
		public Material PavementMaterial;
		public Material GroundMaterial;

		[Header("Street dressing")]
		[Tooltip("Street light prefab. Spacing comes from the district profile, not from here.")]
		public GameObject StreetLight;
		[Tooltip("Props scattered on pavements and in alleys — bins, crates, skips. Cover, and climbable.")]
		public GameObject[] StreetProps;
		[Tooltip("Props scattered on rooftops — vents, AC units, water tanks. Cover on the roof network.")]
		public GameObject[] RoofProps;

		/// <summary>True when this kit claims <paramref name="archetype"/> (or claims everything).</summary>
		public bool Handles(BuildingArchetype archetype)
		{
			if (Archetypes == null || Archetypes.Length == 0) return true;
			for (int i = 0; i < Archetypes.Length; i++)
				if (Archetypes[i] == archetype) return true;
			return false;
		}

		/// <summary>Wall prefab for a module with the given features, or null to fall back to primitives.</summary>
		public GameObject ExteriorPanel(bool hasDoor, bool hasWindow, bool isShutter, bool isShopFront)
		{
			if (isShutter && ExteriorWallShutter != null) return ExteriorWallShutter;
			if (hasDoor && ExteriorWallDoor != null) return ExteriorWallDoor;
			if (isShopFront && ShopFront != null) return ShopFront;
			if (hasWindow && ExteriorWallWindow != null) return ExteriorWallWindow;
			return ExteriorWall;
		}
	}
}
