using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Fills a building's rooms with props — desks, shelving, racks, beds, counters — so interiors read as
	/// places rather than empty boxes. Everything derives from the city seed plus lot/floor/room indices, so
	/// every peer furnishes identically and the editor NavMesh bake (props have colliders) stays valid at
	/// runtime. Props are merged into three meshes per building (one per material) — a furnished tower costs
	/// three draw calls and three static colliders, not hundreds.
	///
	/// <para>Placement respects the room's doorways (a clearance disc around every portal) and the stair bay,
	/// and props never overlap each other. Beyond that it is deliberately loose — a desk at a slightly odd
	/// angle to the wall is what makes a generated office feel inhabited.</para>
	/// </summary>
	public static class RoomFurnisher
	{
		private const float WallMargin = 0.45f;
		private const float PortalClearance = 1.4f;

		// Scratch state, reset per building. Static is safe: generation is strictly single-threaded.
		private static readonly List<Rect> _occupied = new List<Rect>(24);
		private static readonly List<Vector3> _portals = new List<Vector3>(8);

		public static void Furnish(BuildingPlan plan, Transform parent, int seed)
		{
			var wood = new MeshBuilder();
			var metal = new MeshBuilder();
			var soft = new MeshBuilder();

			for (int level = 0; level < plan.Floors.Count; level++)
			{
				var floor = plan.Floors[level];
				for (int r = 0; r < floor.Rooms.Count; r++)
				{
					var room = floor.Rooms[r];
					if (room.Kind is RoomKind.StairCore or RoomKind.Corridor or RoomKind.Bathroom) continue;

					var rng = RunRng.For(seed, RngStream.Content, plan.LotIndex * 977 + level * 31 + r);
					CollectPortals(plan, level, r);
					_occupied.Clear();

					FurnishRoom(room, ref rng, wood, metal, soft);
				}
			}

			Emit(parent, "Props_Wood", wood, CityMaterials.PropWood);
			Emit(parent, "Props_Metal", metal, CityMaterials.PropMetal);
			Emit(parent, "Props_Soft", soft, CityMaterials.PropSoft);
		}

		// =====================================================================
		// Per-room recipes
		// =====================================================================

		private static void FurnishRoom(RoomPlan room, ref RunRng rng, MeshBuilder wood, MeshBuilder metal, MeshBuilder soft)
		{
			switch (room.Kind)
			{
				case RoomKind.Office:
					// Desks with a chair-block tucked behind each, roughly gridded.
					PlaceGrid(room, ref rng, 2.4f, 2.2f, 0.75f, size: new Vector3(1.5f, 0.74f, 0.7f), wood, chairMb: soft);
					PlaceAgainstWall(room, ref rng, new Vector3(0.9f, 1.9f, 0.4f), metal, 2);   // filing cabinets
					break;

				case RoomKind.Conference:
					PlaceCentered(room, new Vector3(
						Mathf.Clamp(room.Area.width * 0.5f, 1.6f, 4f), 0.74f,
						Mathf.Clamp(room.Area.height * 0.4f, 1.1f, 2f)), wood);
					PlaceAgainstWall(room, ref rng, new Vector3(1.6f, 1.1f, 0.35f), metal, 1);  // sideboard
					break;

				case RoomKind.ServerRoom:
					// Rack rows — tall, metallic, aisle between them.
					PlaceRows(room, ref rng, rowSpacing: 1.8f, size: new Vector3(0.7f, 2.0f, 1.9f), metal);
					break;

				case RoomKind.Utility:
					PlaceAgainstWall(room, ref rng, new Vector3(1.1f, 1.8f, 0.35f), metal, 3);  // panels
					PlaceScatter(room, ref rng, new Vector3(0.8f, 0.8f, 0.8f), wood, 1, 0.6f);
					break;

				case RoomKind.Storage:
					PlaceRows(room, ref rng, rowSpacing: 2.0f, size: new Vector3(0.55f, 2.0f, 2.2f), wood);
					PlaceScatter(room, ref rng, new Vector3(0.9f, 0.7f, 0.9f), wood, 3, 0.55f); // crates
					break;

				case RoomKind.Retail:
					PlaceRows(room, ref rng, rowSpacing: 2.2f, size: new Vector3(0.9f, 1.5f, 2.4f), metal);
					PlaceAgainstWall(room, ref rng, new Vector3(2.0f, 0.95f, 0.6f), wood, 1);   // counter
					break;

				case RoomKind.Apartment:
					PlaceAgainstWall(room, ref rng, new Vector3(1.5f, 0.55f, 2.0f), soft, 1);   // bed
					PlaceAgainstWall(room, ref rng, new Vector3(1.1f, 2.0f, 0.6f), wood, 1);    // wardrobe
					PlaceScatter(room, ref rng, new Vector3(1.1f, 0.72f, 1.1f), wood, 1, 0.8f); // table
					PlaceAgainstWall(room, ref rng, new Vector3(1.9f, 0.75f, 0.85f), soft, 1);  // sofa
					break;

				case RoomKind.Lobby:
					PlaceCentered(room, new Vector3(
						Mathf.Clamp(room.Area.width * 0.35f, 1.2f, 3f), 1.05f, 0.7f), wood);    // reception desk
					PlaceScatter(room, ref rng, new Vector3(0.55f, 0.9f, 0.55f), soft, 2, 0.5f); // planters
					break;

				default:
					PlaceScatter(room, ref rng, new Vector3(1.0f, 0.8f, 1.0f), wood, 2, 0.6f);
					break;
			}
		}

		// =====================================================================
		// Placement primitives
		// =====================================================================

		private static void PlaceGrid(RoomPlan room, ref RunRng rng, float spacingX, float spacingZ, float fill,
			Vector3 size, MeshBuilder mb, MeshBuilder chairMb)
		{
			var inner = Shrink(room.Area, WallMargin);
			for (float x = inner.xMin + size.x * 0.5f; x + size.x * 0.5f <= inner.xMax; x += spacingX)
			for (float z = inner.yMin + size.z * 0.5f + 0.6f; z + size.z * 0.5f <= inner.yMax; z += spacingZ)
			{
				if (!rng.Chance(fill)) continue;
				var rect = new Rect(x - size.x * 0.5f, z - size.z * 0.5f, size.x, size.z);
				if (!TryClaim(room, rect)) continue;

				AddProp(mb, rect, room.FloorY, size.y);

				// Chair block behind the desk.
				var chair = new Rect(x - 0.25f, rect.yMin - 0.6f, 0.5f, 0.5f);
				if (TryClaim(room, chair)) AddProp(chairMb, chair, room.FloorY, 0.45f);
			}
		}

		private static void PlaceRows(RoomPlan room, ref RunRng rng, float rowSpacing, Vector3 size, MeshBuilder mb)
		{
			var inner = Shrink(room.Area, WallMargin);
			bool rowsAlongZ = inner.height >= inner.width;
			float rowLength = (rowsAlongZ ? inner.height : inner.width);
			float across = (rowsAlongZ ? inner.width : inner.height);
			float unitLen = rowsAlongZ ? size.z : size.x;

			for (float o = size.x * 0.5f; o + size.x * 0.5f <= across; o += rowSpacing)
			{
				int units = Mathf.FloorToInt(rowLength / (unitLen + 0.1f));
				for (int u = 0; u < units; u++)
				{
					if (!rng.Chance(0.85f)) continue;

					Rect rect = rowsAlongZ
						? new Rect(inner.xMin + o - size.x * 0.5f, inner.yMin + u * (unitLen + 0.1f), size.x, size.z)
						: new Rect(inner.xMin + u * (unitLen + 0.1f), inner.yMin + o - size.x * 0.5f, size.x, size.z);

					if (!TryClaim(room, rect)) continue;
					AddProp(mb, rect, room.FloorY, size.y);
				}
			}
		}

		private static void PlaceAgainstWall(RoomPlan room, ref RunRng rng, Vector3 size, MeshBuilder mb, int count)
		{
			var inner = Shrink(room.Area, 0.12f);
			for (int n = 0; n < count; n++)
			{
				for (int attempt = 0; attempt < 6; attempt++)
				{
					int wall = rng.Range(0, 4);
					float t = rng.Range(0.15f, 0.85f);

					Rect rect = wall switch
					{
						0 => new Rect(Mathf.Lerp(inner.xMin, inner.xMax - size.x, t), inner.yMin, size.x, size.z),
						1 => new Rect(Mathf.Lerp(inner.xMin, inner.xMax - size.x, t), inner.yMax - size.z, size.x, size.z),
						2 => new Rect(inner.xMin, Mathf.Lerp(inner.yMin, inner.yMax - size.x, t), size.z, size.x),
						_ => new Rect(inner.xMax - size.z, Mathf.Lerp(inner.yMin, inner.yMax - size.x, t), size.z, size.x),
					};

					if (!TryClaim(room, rect)) continue;
					AddProp(mb, rect, room.FloorY, size.y);
					break;
				}
			}
		}

		private static void PlaceCentered(RoomPlan room, Vector3 size, MeshBuilder mb)
		{
			var rect = new Rect(room.Area.center.x - size.x * 0.5f, room.Area.center.y - size.z * 0.5f, size.x, size.z);
			if (TryClaim(room, rect)) AddProp(mb, rect, room.FloorY, size.y);
		}

		private static void PlaceScatter(RoomPlan room, ref RunRng rng, Vector3 size, MeshBuilder mb, int count, float chance)
		{
			var inner = Shrink(room.Area, WallMargin);
			if (inner.width < size.x || inner.height < size.z) return;

			for (int n = 0; n < count; n++)
			{
				if (!rng.Chance(chance)) continue;
				for (int attempt = 0; attempt < 5; attempt++)
				{
					var rect = new Rect(
						rng.Range(inner.xMin, inner.xMax - size.x),
						rng.Range(inner.yMin, inner.yMax - size.z),
						size.x, size.z);
					if (!TryClaim(room, rect)) continue;
					AddProp(mb, rect, room.FloorY, size.y);
					break;
				}
			}
		}

		// =====================================================================
		// Shared mechanics
		// =====================================================================

		/// <summary>A spot is claimable when it fits the room, keeps every doorway clear, and overlaps nothing
		/// already placed. Claiming records it so later props route around it.</summary>
		private static bool TryClaim(RoomPlan room, Rect rect)
		{
			if (rect.xMin < room.Area.xMin || rect.xMax > room.Area.xMax) return false;
			if (rect.yMin < room.Area.yMin || rect.yMax > room.Area.yMax) return false;

			var padded = Shrink(rect, -0.25f); // breathing room between props
			for (int i = 0; i < _occupied.Count; i++)
				if (padded.Overlaps(_occupied[i])) return false;

			for (int i = 0; i < _portals.Count; i++)
			{
				var p = _portals[i];
				float dx = Mathf.Clamp(p.x, rect.xMin, rect.xMax) - p.x;
				float dz = Mathf.Clamp(p.z, rect.yMin, rect.yMax) - p.z;
				if (dx * dx + dz * dz < PortalClearance * PortalClearance) return false;
			}

			_occupied.Add(rect);
			return true;
		}

		private static void CollectPortals(BuildingPlan plan, int level, int roomIndex)
		{
			_portals.Clear();
			for (int i = 0; i < plan.Portals.Count; i++)
			{
				var p = plan.Portals[i];
				bool touches = (p.FromLevel == level && p.FromRoom == roomIndex) ||
					(p.ToLevel == level && p.ToRoom == roomIndex);
				if (touches) _portals.Add(p.Position);
			}
		}

		private static void AddProp(MeshBuilder mb, Rect footprint, float floorY, float height)
		{
			mb.AddBox(
				new Vector3(footprint.xMin, floorY, footprint.yMin),
				new Vector3(footprint.xMax, floorY + height, footprint.yMax));
		}

		private static Rect Shrink(Rect r, float by) =>
			Rect.MinMaxRect(r.xMin + by, r.yMin + by, r.xMax - by, r.yMax - by);

		private static void Emit(Transform parent, string name, MeshBuilder builder, Material material)
		{
			if (builder.IsEmpty) return;

			var mesh = builder.ToMesh(name);
			if (mesh == null) return;

			var go = new GameObject(name);
			go.transform.SetParent(parent, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = material;
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
			go.AddComponent<RuntimeMesh>().Mesh = mesh;
		}
	}
}
