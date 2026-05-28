using UnityEngine;

namespace Starter.Common.Inventory
{
	[System.Flags]
	public enum PlacementSurfaces
	{
		None    = 0,
		Ground  = 1 << 0,
		Wall    = 1 << 1,
		Ceiling = 1 << 2,
		All     = Ground | Wall | Ceiling,
	}

	/// <summary>
	/// Item that can be placed into the world from the player's hand. Placeables are
	/// carried in a dedicated channel on Inventory (not deposited into hotbar slots).
	/// The local PlacementController reads these rules to drive ghost validation; the
	/// inventory's RPC_RequestPlaceCarried re-checks them on the state authority before
	/// spawning <see cref="PlacedPrefab"/>.
	/// </summary>
	[CreateAssetMenu(fileName = "Placeable", menuName = "Inventory/Placeable Definition", order = 2)]
	public sealed class PlaceableDefinition : ItemDefinition
	{
		[Header("Placement")]
		[Tooltip("NetworkObject prefab spawned via Runner.Spawn when placement is confirmed. Must have a NetworkObject component.")]
		public GameObject PlacedPrefab;

		[Tooltip("Local-only visual prefab used for the placement ghost. If empty, PlacedPrefab is cloned and stripped of colliders/NetworkObject.")]
		public GameObject GhostPrefab;

		[Tooltip("Which surface orientations this item can be placed on.")]
		public PlacementSurfaces AllowedSurfaces = PlacementSurfaces.Ground;

		[Tooltip("Layers a raycast is allowed to hit as a valid placement surface.")]
		public LayerMask PlacementMask = ~0;

		[Tooltip("Max distance (meters) from the camera the placement raycast considers.")]
		public float PlacementRange = 2.5f;

		[Tooltip("Sphere radius (meters) used to check that the placement spot is clear of other colliders. 0 = no clearance check.")]
		public float Footprint = 0.3f;

		[Tooltip("Max angle (degrees) between surface normal and world up to still count as 'Ground'. Anything between this and (180 - this) is treated as 'Wall'.")]
		[Range(0f, 89f)] public float MaxGroundAngle = 30f;

		[Tooltip("If true, the placed object's up axis aligns with the surface normal (lays flat on walls/ceilings). If false, it always stands upright on world Y.")]
		public bool AlignToSurface = true;

		[Tooltip("Yaw speed (degrees per second) around the surface normal while the Rotate key is held.")]
		public float RotationSpeed = 90f;

		public bool SurfaceAllowed(PlacementSurfaces kind) => (AllowedSurfaces & kind) != 0;

		/// <summary>Classify a surface from its world-space normal using <see cref="MaxGroundAngle"/>.</summary>
		public PlacementSurfaces ClassifySurface(Vector3 worldNormal)
		{
			float angle = Vector3.Angle(worldNormal, Vector3.up);
			if (angle <= MaxGroundAngle) return PlacementSurfaces.Ground;
			if (angle >= 180f - MaxGroundAngle) return PlacementSurfaces.Ceiling;
			return PlacementSurfaces.Wall;
		}
	}
}
