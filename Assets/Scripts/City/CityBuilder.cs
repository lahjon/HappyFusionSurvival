using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Turns layout data into geometry.
	///
	/// <para><b>Two representations per building.</b> Every building always exists as a solid <i>shell</i> — one
	/// instanced box with a collider, cheap enough that a thousand of them cost almost nothing and enough to give
	/// the city a skyline, block sightlines and carry a walkable roof. When players get close, the shell is
	/// swapped for a <i>realised</i> building: hollow walls, floor slabs, doorways, stairs. Nothing is ever
	/// rendered twice, and the swap is a pair of SetActive calls.</para>
	///
	/// <para>All geometry is derived from the seed, so it is built locally on every peer and never replicated.
	/// The network only carries what players have <em>done</em> to it.</para>
	/// </summary>
	public static class CityBuilder
	{
		private const float RoadY = 0.02f;
		private const float PavementY = 0.12f;
		private const float SlabThickness = 0.3f;

		private static Mesh _unitCube;

		// =====================================================================
		// Ground plane, roads, open lots
		// =====================================================================

		/// <summary>
		/// Builds the ground plane, every road surface and every open lot as a handful of merged meshes. This is
		/// static for the whole match — it is the one part of the city that is always present, because it is what
		/// players navigate by.
		/// </summary>
		public static GameObject BuildGround(CityLayout layout, BuildingKit kit, Transform parent)
		{
			var root = new GameObject("Ground");
			root.transform.SetParent(parent, false);

			var ground = new MeshBuilder();
			ground.AddHorizontalQuad(layout.Bounds, 0f, 0.05f);
			CreateMeshObject("Terrain", ground, CityMaterials.Or(kit != null ? kit.GroundMaterial : null, CityMaterials.Ground), root.transform, true);

			// Roads are grouped by class so an alley can read darker and rougher than an avenue without a
			// per-segment material.
			BuildRoadClass(layout, RoadClass.Arterial, kit, root.transform, "Roads_Arterial");
			BuildRoadClass(layout, RoadClass.Street, kit, root.transform, "Roads_Street");
			BuildRoadClass(layout, RoadClass.Alley, kit, root.transform, "Roads_Alley");

			// Pavements and open lots — one merged mesh, raised slightly so kerbs read at ground level.
			var pavement = new MeshBuilder();
			for (int i = 0; i < layout.Lots.Count; i++)
			{
				var lot = layout.Lots[i];
				if (lot.Use == LotUse.Building) continue;
				pavement.AddHorizontalQuad(lot.Area, PavementY, 0.15f);
			}
			CreateMeshObject("OpenLots", pavement, CityMaterials.Or(kit != null ? kit.PavementMaterial : null, CityMaterials.Pavement), root.transform, true);

			return root;
		}

		private static void BuildRoadClass(CityLayout layout, RoadClass cls, BuildingKit kit, Transform parent, string name)
		{
			var builder = new MeshBuilder();
			for (int i = 0; i < layout.Roads.Count; i++)
			{
				if (layout.Roads[i].Class != cls) continue;
				builder.AddHorizontalQuad(layout.Roads[i].Area, RoadY, 0.12f);
			}
			CreateMeshObject(name, builder, CityMaterials.Or(kit != null ? kit.RoadMaterial : null, CityMaterials.Road), parent, false);
		}

		// =====================================================================
		// Building shells (always on)
		// =====================================================================

		/// <summary>
		/// The cheap always-present form of a building: a solid box with a collider. Uses a shared unit cube so
		/// every shell in the city can batch, and carries the roof surface players walk on.
		/// </summary>
		public static GameObject BuildShell(CityLot lot, BuildingKit kit, Transform parent)
		{
			var plan = lot.Building;
			if (plan == null) return null;

			float height = plan.PlannedFloors * plan.FloorHeight;

			var go = new GameObject($"Shell_{plan.Name}");
			go.transform.SetParent(parent, false);
			go.transform.position = new Vector3(plan.Footprint.center.x, plan.BaseY + height * 0.5f, plan.Footprint.center.y);
			go.transform.localScale = new Vector3(plan.Footprint.width, height, plan.Footprint.height);

			var filter = go.AddComponent<MeshFilter>();
			filter.sharedMesh = UnitCube();

			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = CityMaterials.Or(kit != null ? kit.WallMaterial : null, CityMaterials.WallVariant(plan.LotIndex));

			go.AddComponent<BoxCollider>();
			return go;
		}

		// =====================================================================
		// Realised buildings (streamed in)
		// =====================================================================

		/// <summary>
		/// Builds the walk-in version of a building from its floorplan: slabs, exterior shell with real openings,
		/// interior partitions, stairs, roof and parapet. Returns a root the streamer can destroy wholesale.
		/// </summary>
		public static GameObject BuildInterior(CityLot lot, BuildingKit kit, Transform parent, int seed)
		{
			var plan = lot.Building;
			if (plan == null) return null;

			BuildingPlanGenerator.EnsureInterior(plan, seed);

			var root = new GameObject($"Building_{plan.Name}");
			root.transform.SetParent(parent, false);

			var structure = new MeshBuilder();
			var slabs = new MeshBuilder();

			for (int level = 0; level < plan.PlannedFloors; level++)
			{
				var floor = plan.Floor(level);
				if (floor == null) continue;

				AddFloorSlab(slabs, plan, floor);
				AddInteriorWalls(structure, plan, floor);
				AddExteriorWalls(structure, plan, floor);
				AddStairs(structure, plan, floor);
			}

			AddRoof(slabs, structure, plan);

			CreateMeshObject("Structure", structure, CityMaterials.Or(kit != null ? kit.WallMaterial : null, CityMaterials.WallVariant(plan.LotIndex)), root.transform, true);
			CreateMeshObject("Slabs", slabs, CityMaterials.Or(kit != null ? kit.FloorMaterial : null, CityMaterials.Floor), root.transform, true);

			RoomFurnisher.Furnish(plan, root.transform, seed);
			AddFireEscapes(plan, kit, root.transform);
			AddBuildingLights(plan, lot, root.transform, seed);

			return root;
		}

		// =====================================================================
		// Lighting
		// =====================================================================

		/// <summary>
		/// Entrance and interior lighting for one building. Entrances always get a lamp — a lit doorway is how
		/// players read "this is a way in" from across a dark street. Inside, circulation (lobby, corridors,
		/// stair cores) is lit and a deterministic minority of other rooms flicker on, so buildings read as
		/// half-dead rather than pitch black. Every Light goes through <see cref="CityLightCuller"/>; the
		/// emissive fixture geometry stays visible when the Light itself is culled.
		/// </summary>
		private static void AddBuildingLights(BuildingPlan plan, CityLot lot, Transform parent, int seed)
		{
			var root = new GameObject("Lights");
			root.transform.SetParent(parent, false);

			// --- Entrances -----------------------------------------------------
			for (int i = 0; i < plan.Portals.Count; i++)
			{
				var portal = plan.Portals[i];
				if (!portal.LeadsOutside || portal.ToLevel != 0) continue;
				if (portal.Kind is not (PortalKind.Door or PortalKind.Shutter)) continue;

				Vector3 outward = portal.Facing.sqrMagnitude > 0.001f
					? new Vector3(portal.Facing.x, 0f, portal.Facing.z).normalized
					: Vector3.forward;
				Vector3 fixturePos = portal.Position + Vector3.up * (CityConstants.DoorHeight + 0.35f) + outward * 0.25f;

				AddFixture(root.transform, fixturePos, new Vector3(0.5f, 0.12f, 0.22f));
				AddSpot(root.transform, fixturePos + outward * 0.1f, Quaternion.LookRotation(Vector3.down, outward),
					range: 9f, angle: 100f, intensity: 2.4f, new Color(1f, 0.88f, 0.7f));
			}

			// --- Interiors ------------------------------------------------------
			var rng = RunRng.For(seed, RngStream.Content, lot.Index, 71);

			for (int level = 0; level < plan.PlannedFloors; level++)
			{
				var floor = plan.Floor(level);
				if (floor == null) continue;

				for (int r = 0; r < floor.Rooms.Count; r++)
				{
					var room = floor.Rooms[r];
					if (room.Kind == RoomKind.Roof) continue;

					// Circulation is always lit; other rooms roll. The roll is per-room deterministic, so every
					// peer (and every rebuild of this building) lights the same rooms.
					bool lit = room.Kind is RoomKind.Corridor or RoomKind.StairCore or RoomKind.Lobby
						|| rng.Chance(0.3f);
					if (!lit) continue;

					float ceilingY = room.FloorY + plan.FloorHeight - SlabThickness - 0.15f;
					var center = new Vector3(room.Area.center.x, ceilingY, room.Area.center.y);

					AddFixture(root.transform, center, new Vector3(0.9f, 0.08f, 0.25f));

					float radius = Mathf.Min(14f, Mathf.Max(room.Area.width, room.Area.height) * 0.9f + 2f);
					AddSpot(root.transform, center - Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.down),
						range: radius, angle: 130f, intensity: 1.6f, new Color(0.9f, 0.97f, 0.9f));
				}
			}
		}

		private static void AddFixture(Transform parent, Vector3 position, Vector3 size)
		{
			var go = new GameObject("Fixture");
			go.transform.SetParent(parent, false);
			go.transform.position = position;

			var mb = new MeshBuilder();
			mb.AddBox(-size * 0.5f, size * 0.5f, 1f);
			go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("FixtureMesh");
			go.AddComponent<MeshRenderer>().sharedMaterial = CityMaterials.InteriorFixture;
			go.AddComponent<RuntimeMesh>().Mesh = go.GetComponent<MeshFilter>().sharedMesh;
		}

		private static void AddSpot(Transform parent, Vector3 position, Quaternion rotation,
			float range, float angle, float intensity, Color color)
		{
			var go = new GameObject("Light");
			go.transform.SetParent(parent, false);
			go.transform.SetPositionAndRotation(position, rotation);

			var light = go.AddComponent<Light>();
			light.type = LightType.Spot;
			light.range = range;
			light.spotAngle = angle;
			light.innerSpotAngle = angle * 0.55f;
			light.intensity = intensity;
			light.color = color;
			light.shadows = LightShadows.None;

			CityLightCuller.Register(light);
		}

		/// <summary>Floor slab, with the stairwell opening carved out above any stairs climbing up from the floor
		/// below. Without the opening the stairs are decorative — you climb them into a ceiling.</summary>
		private static void AddFloorSlab(MeshBuilder mb, BuildingPlan plan, FloorPlan floor)
		{
			float y = floor.FloorY;
			var area = plan.Footprint;

			_holeScratch.Clear();
			if (floor.Level > 0 && HasStairsFrom(plan, floor.Level - 1))
			{
				var coreBelow = plan.Floor(floor.Level - 1)?.FindRoom(RoomKind.StairCore);
				if (coreBelow != null) StairHoles(coreBelow, _holeScratch);
			}

			AddSlabArea(mb, area, y, _holeScratch, 0);
		}

		private static readonly List<Rect> _holeScratch = new List<Rect>(2);

		/// <summary>Emits a slab covering <paramref name="area"/> minus the hole rects, by recursive rectangle
		/// subtraction — no polygon clipper needed for a couple of axis-aligned openings.</summary>
		private static void AddSlabArea(MeshBuilder mb, Rect area, float yTop, List<Rect> holes, int start)
		{
			if (area.width <= 0.01f || area.height <= 0.01f) return;

			while (start < holes.Count && !area.Overlaps(holes[start]))
				start++;

			if (start >= holes.Count)
			{
				mb.AddBox(
					new Vector3(area.xMin, yTop - SlabThickness, area.yMin),
					new Vector3(area.xMax, yTop, area.yMax));
				return;
			}

			var hole = holes[start];
			float hx0 = Mathf.Max(area.xMin, hole.xMin), hx1 = Mathf.Min(area.xMax, hole.xMax);
			float hz0 = Mathf.Max(area.yMin, hole.yMin), hz1 = Mathf.Min(area.yMax, hole.yMax);

			AddSlabArea(mb, Rect.MinMaxRect(area.xMin, area.yMin, hx0, area.yMax), yTop, holes, start + 1);
			AddSlabArea(mb, Rect.MinMaxRect(hx1, area.yMin, area.xMax, area.yMax), yTop, holes, start + 1);
			AddSlabArea(mb, Rect.MinMaxRect(hx0, area.yMin, hx1, hz0), yTop, holes, start + 1);
			AddSlabArea(mb, Rect.MinMaxRect(hx0, hz1, hx1, area.yMax), yTop, holes, start + 1);
		}

		/// <summary>
		/// Walls between rooms. Built by walking the floor's cell grid and emitting a panel wherever two adjacent
		/// cells belong to different rooms — which guarantees exactly one wall per boundary, with no duplicates
		/// and no gaps, without any geometric de-duplication pass.
		/// </summary>
		private static void AddInteriorWalls(MeshBuilder mb, BuildingPlan plan, FloorPlan floor)
		{
			int w = plan.GridSize.x, h = plan.GridSize.y;
			var owner = BuildCellMap(plan, floor);
			float wallHeight = plan.FloorHeight - SlabThickness;

			for (int z = 0; z < h; z++)
			for (int x = 0; x < w; x++)
			{
				int here = owner[z * w + x];
				if (here < 0) continue;

				// +X boundary
				if (x + 1 < w && owner[z * w + x + 1] != here)
				{
					float wx = plan.Footprint.xMin + (x + 1) * CityConstants.Module;
					float z0 = plan.Footprint.yMin + z * CityConstants.Module;
					AddWall(mb, new Vector3(wx, floor.FloorY, z0),
						new Vector3(wx, floor.FloorY, z0 + CityConstants.Module),
						wallHeight, HasPortalNear(plan, floor.Level, new Vector3(wx, floor.FloorY, z0 + CityConstants.Module * 0.5f)));
				}

				// +Z boundary
				if (z + 1 < h && owner[(z + 1) * w + x] != here)
				{
					float wz = plan.Footprint.yMin + (z + 1) * CityConstants.Module;
					float x0 = plan.Footprint.xMin + x * CityConstants.Module;
					AddWall(mb, new Vector3(x0, floor.FloorY, wz),
						new Vector3(x0 + CityConstants.Module, floor.FloorY, wz),
						wallHeight, HasPortalNear(plan, floor.Level, new Vector3(x0 + CityConstants.Module * 0.5f, floor.FloorY, wz)));
				}
			}
		}

		/// <summary>The building's outer skin, with the ways in punched through it.</summary>
		private static void AddExteriorWalls(MeshBuilder mb, BuildingPlan plan, FloorPlan floor)
		{
			int w = plan.GridSize.x, h = plan.GridSize.y;
			float wallHeight = plan.FloorHeight - SlabThickness;
			float m = CityConstants.Module;

			for (int x = 0; x < w; x++)
			{
				float x0 = plan.Footprint.xMin + x * m;

				AddWall(mb, new Vector3(x0, floor.FloorY, plan.Footprint.yMin),
					new Vector3(x0 + m, floor.FloorY, plan.Footprint.yMin), wallHeight,
					HasPortalNear(plan, floor.Level, new Vector3(x0 + m * 0.5f, floor.FloorY, plan.Footprint.yMin)));

				AddWall(mb, new Vector3(x0, floor.FloorY, plan.Footprint.yMax),
					new Vector3(x0 + m, floor.FloorY, plan.Footprint.yMax), wallHeight,
					HasPortalNear(plan, floor.Level, new Vector3(x0 + m * 0.5f, floor.FloorY, plan.Footprint.yMax)));
			}

			for (int z = 0; z < h; z++)
			{
				float z0 = plan.Footprint.yMin + z * m;

				AddWall(mb, new Vector3(plan.Footprint.xMin, floor.FloorY, z0),
					new Vector3(plan.Footprint.xMin, floor.FloorY, z0 + m), wallHeight,
					HasPortalNear(plan, floor.Level, new Vector3(plan.Footprint.xMin, floor.FloorY, z0 + m * 0.5f)));

				AddWall(mb, new Vector3(plan.Footprint.xMax, floor.FloorY, z0),
					new Vector3(plan.Footprint.xMax, floor.FloorY, z0 + m), wallHeight,
					HasPortalNear(plan, floor.Level, new Vector3(plan.Footprint.xMax, floor.FloorY, z0 + m * 0.5f)));
			}
		}

		// =====================================================================
		// Stairs — a compact two-flight switchback in a corner of the stair core
		// =====================================================================

		private const float StairWidth = 1.25f;
		private const float StairLanding = 1.5f;
		private const float StairInset = 0.2f;
		private const int StepsPerFlight = 10;

		/// <summary>The strip of the stair core the switchback occupies: two flights side by side plus the far
		/// landing, hugging the core's min corner. Everything about the stairs — steps, slab openings, prop
		/// exclusion — derives from this one rect so they can never disagree.</summary>
		private static Rect StairBay(RoomPlan core)
		{
			var a = core.Area;
			bool alongZ = a.height >= a.width;
			float cross = StairWidth * 2f + 0.1f;

			return alongZ
				? new Rect(a.xMin + StairInset, a.yMin + StairInset,
					Mathf.Min(cross, a.width - StairInset * 2f), a.height - StairInset * 2f)
				: new Rect(a.xMin + StairInset, a.yMin + StairInset,
					a.width - StairInset * 2f, Mathf.Min(cross, a.height - StairInset * 2f));
		}

		private static bool HasStairsFrom(BuildingPlan plan, int level)
		{
			var floor = plan.Floor(level);
			if (floor == null || floor.FindRoom(RoomKind.StairCore) == null) return false;
			if (level + 1 > plan.PlannedFloors) return false;
			if (level + 1 == plan.PlannedFloors && !plan.HasRoofAccess) return false;
			return true;
		}

		/// <summary>
		/// The slab/roof openings above a stair bay: the whole return flight's strip, and the upper part of the
		/// first flight's strip. The slab kept over the first flight's lower half is the surface you step off
		/// onto — and the openings guarantee head-room the moment either flight climbs above chest height.
		/// </summary>
		private static void StairHoles(RoomPlan core, List<Rect> holes)
		{
			var bay = StairBay(core);
			bool alongZ = core.Area.height >= core.Area.width;
			float flightLen = (alongZ ? bay.height : bay.width) - StairLanding;
			float cut = flightLen * 0.45f;

			if (alongZ)
			{
				holes.Add(new Rect(bay.xMin + StairWidth + 0.1f, bay.yMin, StairWidth, bay.height));
				holes.Add(new Rect(bay.xMin, bay.yMin + cut, StairWidth + 0.1f, bay.height - cut));
			}
			else
			{
				holes.Add(new Rect(bay.xMin, bay.yMin + StairWidth + 0.1f, bay.width, StairWidth));
				holes.Add(new Rect(bay.xMin + cut, bay.yMin, bay.width - cut, StairWidth + 0.1f));
			}
		}

		/// <summary>
		/// Two flights of solid box steps with a half-height landing between them. Solid boxes (each step is a
		/// full column down to the floor) give the character controller and the NavMesh bake an unambiguous
		/// stepped surface — no thin ramps, no gaps — at a gentle ~32° pitch, in a quarter of the room the old
		/// full-room ramp ate.
		/// </summary>
		private static void AddStairs(MeshBuilder mb, BuildingPlan plan, FloorPlan floor)
		{
			if (!HasStairsFrom(plan, floor.Level)) return;
			var core = floor.FindRoom(RoomKind.StairCore);

			var bay = StairBay(core);
			bool alongZ = core.Area.height >= core.Area.width;

			float u0 = alongZ ? bay.yMin : bay.xMin;
			float u1 = alongZ ? bay.yMax : bay.xMax;
			float v0 = alongZ ? bay.xMin : bay.yMin;

			float y0 = floor.FloorY;
			float half = plan.FloorHeight * 0.5f;
			float flightLen = (u1 - u0) - StairLanding;
			float rise = half / StepsPerFlight;
			float run = flightLen / StepsPerFlight;

			// First flight climbs away from the entry edge…
			for (int i = 0; i < StepsPerFlight; i++)
			{
				AddBayBox(mb, alongZ,
					u0 + i * run, u0 + (i + 1) * run,
					v0, v0 + StairWidth,
					y0, y0 + (i + 1) * rise);
			}

			// …to a full-width landing at half height…
			AddBayBox(mb, alongZ, u0 + flightLen, u1, v0, v0 + StairWidth * 2f + 0.1f, y0, y0 + half);

			// …and the return flight climbs back to the opening in the slab above.
			for (int i = 0; i < StepsPerFlight; i++)
			{
				AddBayBox(mb, alongZ,
					u0 + flightLen - (i + 1) * run, u0 + flightLen - i * run,
					v0 + StairWidth + 0.1f, v0 + StairWidth * 2f + 0.1f,
					y0, y0 + half + (i + 1) * rise);
			}
		}

		/// <summary>Box in the stair bay's local frame: u along the flights, v across them.</summary>
		private static void AddBayBox(MeshBuilder mb, bool alongZ, float uMin, float uMax, float vMin, float vMax, float yMin, float yMax)
		{
			if (alongZ) mb.AddBox(new Vector3(vMin, yMin, uMin), new Vector3(vMax, yMax, uMax));
			else mb.AddBox(new Vector3(uMin, yMin, vMin), new Vector3(uMax, yMax, vMax));
		}

		/// <summary>Roof slab plus its parapet — the cover that makes rooftop routes survivable. When the plan has
		/// roof access, the top stairwell's opening is carved through the roof slab too.</summary>
		private static void AddRoof(MeshBuilder slabs, MeshBuilder structure, BuildingPlan plan)
		{
			float y = plan.RoofY;

			_holeScratch.Clear();
			if (plan.HasRoofAccess && HasStairsFrom(plan, plan.PlannedFloors - 1))
			{
				var topCore = plan.Floor(plan.PlannedFloors - 1)?.FindRoom(RoomKind.StairCore);
				if (topCore != null) StairHoles(topCore, _holeScratch);
			}

			AddSlabArea(slabs, plan.Footprint, y, _holeScratch, 0);

			float t = 0.25f;
			float top = y + plan.ParapetHeight;
			var f = plan.Footprint;

			structure.AddBox(new Vector3(f.xMin, y, f.yMin), new Vector3(f.xMax, top, f.yMin + t));
			structure.AddBox(new Vector3(f.xMin, y, f.yMax - t), new Vector3(f.xMax, top, f.yMax));
			structure.AddBox(new Vector3(f.xMin, y, f.yMin), new Vector3(f.xMin + t, top, f.yMax));
			structure.AddBox(new Vector3(f.xMax - t, y, f.yMin), new Vector3(f.xMax, top, f.yMax));
		}

		/// <summary>Instantiates fire escape sections up the facade sides the plan marked. These are the traversal
		/// spine of a locked-out run, so they are real geometry rather than a climb volume.</summary>
		private static void AddFireEscapes(BuildingPlan plan, BuildingKit kit, Transform parent)
		{
			if (kit == null || kit.FireEscape == null || plan.FireEscapeSides.Count == 0) return;

			for (int s = 0; s < plan.FireEscapeSides.Count; s++)
			{
				int side = plan.FireEscapeSides[s];
				GetSideAnchor(plan, side, out Vector3 anchor, out Quaternion rotation);

				for (int level = 1; level <= plan.PlannedFloors; level++)
				{
					var position = anchor + Vector3.up * (plan.BaseY + level * plan.FloorHeight);
					Object.Instantiate(kit.FireEscape, position, rotation, parent);
				}
			}
		}

		private static void GetSideAnchor(BuildingPlan plan, int side, out Vector3 anchor, out Quaternion rotation)
		{
			var f = plan.Footprint;
			switch (side)
			{
				case 0:  anchor = new Vector3(f.center.x, 0f, f.yMin); rotation = Quaternion.Euler(0f, 180f, 0f); break;
				case 1:  anchor = new Vector3(f.xMax, 0f, f.center.y); rotation = Quaternion.Euler(0f, 90f, 0f);  break;
				case 2:  anchor = new Vector3(f.center.x, 0f, f.yMax); rotation = Quaternion.identity;            break;
				default: anchor = new Vector3(f.xMin, 0f, f.center.y); rotation = Quaternion.Euler(0f, -90f, 0f); break;
			}
		}

		// =====================================================================
		// Wall primitive
		// =====================================================================

		/// <summary>
		/// One module of wall between <paramref name="p0"/> and <paramref name="p1"/>. When a portal sits on the
		/// panel it is built as jambs around a hole — a full doorway for doors/shutters/thresholds, or a raised
		/// window opening (spandrel below the sill, header above) for windows and fire-escape entries, so climbing
		/// in through one means mantling over the sill rather than strolling through a floor-level gap.
		/// </summary>
		private static void AddWall(MeshBuilder mb, Vector3 p0, Vector3 p1, float height, PortalKind? portal)
		{
			float t = CityConstants.WallThickness * 0.5f;
			bool alongX = !Mathf.Approximately(p0.x, p1.x);

			float y0 = p0.y;
			float y1 = p0.y + height;

			if (portal == null)
			{
				Vector3 min = alongX
					? new Vector3(Mathf.Min(p0.x, p1.x), y0, p0.z - t)
					: new Vector3(p0.x - t, y0, Mathf.Min(p0.z, p1.z));
				Vector3 max = alongX
					? new Vector3(Mathf.Max(p0.x, p1.x), y1, p0.z + t)
					: new Vector3(p0.x + t, y1, Mathf.Max(p0.z, p1.z));
				mb.AddBox(min, max);
				return;
			}

			bool isWindow = portal is PortalKind.Window or PortalKind.FireEscape;
			float half = (isWindow ? CityConstants.WindowWidth : CityConstants.DoorWidth) * 0.5f;
			float holeBottom = isWindow ? y0 + CityConstants.WindowSillHeight : y0;
			float holeTop = y0 + (isWindow ? CityConstants.WindowHeadHeight : CityConstants.DoorHeight);

			if (alongX)
			{
				float a = Mathf.Min(p0.x, p1.x), b = Mathf.Max(p0.x, p1.x);
				float mid = (a + b) * 0.5f;
				mb.AddBox(new Vector3(a, y0, p0.z - t), new Vector3(mid - half, y1, p0.z + t));
				mb.AddBox(new Vector3(mid + half, y0, p0.z - t), new Vector3(b, y1, p0.z + t));
				if (holeBottom > y0)
					mb.AddBox(new Vector3(mid - half, y0, p0.z - t), new Vector3(mid + half, holeBottom, p0.z + t));
				if (holeTop < y1)
					mb.AddBox(new Vector3(mid - half, holeTop, p0.z - t), new Vector3(mid + half, y1, p0.z + t));
			}
			else
			{
				float a = Mathf.Min(p0.z, p1.z), b = Mathf.Max(p0.z, p1.z);
				float mid = (a + b) * 0.5f;
				mb.AddBox(new Vector3(p0.x - t, y0, a), new Vector3(p0.x + t, y1, mid - half));
				mb.AddBox(new Vector3(p0.x - t, y0, mid + half), new Vector3(p0.x + t, y1, b));
				if (holeBottom > y0)
					mb.AddBox(new Vector3(p0.x - t, y0, mid - half), new Vector3(p0.x + t, holeBottom, mid + half));
				if (holeTop < y1)
					mb.AddBox(new Vector3(p0.x - t, holeTop, mid - half), new Vector3(p0.x + t, y1, mid + half));
			}
		}

		/// <summary>The portal sitting on this wall panel, if any — i.e. whether the panel needs a hole in it,
		/// and what shape of hole.</summary>
		private static PortalKind? HasPortalNear(BuildingPlan plan, int level, Vector3 wallCenter)
		{
			const float Tolerance = CityConstants.Module * 0.5f;

			for (int i = 0; i < plan.Portals.Count; i++)
			{
				var p = plan.Portals[i];
				if (p.IsVertical) continue;
				if (p.FromLevel != level && p.ToLevel != level) continue;
				if (p.Kind is PortalKind.Stair or PortalKind.Elevator or PortalKind.RoofHatch) continue;

				if (Mathf.Abs(p.Position.x - wallCenter.x) > Tolerance) continue;
				if (Mathf.Abs(p.Position.z - wallCenter.z) > Tolerance) continue;
				return p.Kind;
			}
			return null;
		}

		/// <summary>Cell → room index map for a floor. -1 where no room covers the cell.</summary>
		private static int[] BuildCellMap(BuildingPlan plan, FloorPlan floor)
		{
			int w = plan.GridSize.x, h = plan.GridSize.y;
			var owner = new int[w * h];
			for (int i = 0; i < owner.Length; i++) owner[i] = -1;

			for (int r = 0; r < floor.Rooms.Count; r++)
			{
				var cells = floor.Rooms[r].Cells;
				for (int z = cells.yMin; z < cells.yMax; z++)
				for (int x = cells.xMin; x < cells.xMax; x++)
				{
					if (x < 0 || z < 0 || x >= w || z >= h) continue;
					owner[z * w + x] = r;
				}
			}
			return owner;
		}

		// =====================================================================
		// Utilities
		// =====================================================================

		private static GameObject CreateMeshObject(string name, MeshBuilder builder, Material material,
			Transform parent, bool collider)
		{
			var mesh = builder.ToMesh(name);
			if (mesh == null) return null;

			var go = new GameObject(name);
			go.transform.SetParent(parent, false);

			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			if (material != null) renderer.sharedMaterial = material;

			if (collider)
				go.AddComponent<MeshCollider>().sharedMesh = mesh;

			// Runtime meshes are native allocations that outlive their GameObject. The streamer destroys building
			// interiors constantly, so without an explicit owner every rebuilt block leaks permanently.
			go.AddComponent<RuntimeMesh>().Mesh = mesh;

			return go;
		}

		/// <summary>Shared 1&#215;1&#215;1 cube, built once. Every building shell in the city uses it, so they all
		/// batch into a handful of draw calls instead of one per building.</summary>
		private static Mesh UnitCube()
		{
			if (_unitCube != null) return _unitCube;

			var builder = new MeshBuilder();
			builder.AddBox(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), 1f);
			_unitCube = builder.ToMesh("CityUnitCube");
			return _unitCube;
		}
	}
}
