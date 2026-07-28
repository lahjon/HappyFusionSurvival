using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Turns a run seed into a complete <see cref="CityLayout"/>: arterial avenues, districts, a street grid,
	/// blocks, alleys, lots and building shells. Pure C#, no Unity objects, no allocation of anything the GC
	/// will care about mid-match — it runs once at match start on every peer and produces byte-identical output.
	///
	/// <para><b>Why BSP and not a road-network sim.</b> A recursive rectangular split gives three properties the
	/// game needs and organic road generators fight you on: every block is convex (so "am I in this block" is one
	/// rect test), every street runs edge to edge (so the grid is guaranteed connected without a repair pass), and
	/// street-to-street sightlines are long and straight — which is what makes crossing a road feel exposed and
	/// makes rooftop routes obviously preferable.</para>
	///
	/// <para><b>Block anatomy.</b> Blocks are subdivided as a buildable perimeter band around an open core, not
	/// as a solid slab of lots. That is how real city blocks work, and it hands the game a courtyard behind every
	/// street frontage — a dark, enclosed second circulation layer for players who want to stay off the avenue.</para>
	/// </summary>
	public static class CityLayoutGenerator
	{
		/// <summary>Hard cap on BSP recursion. Guards against a degenerate settings combination producing an
		/// unbounded split; never reached with sane values.</summary>
		private const int MaxDepth = 12;

		/// <summary>Smallest buildable footprint edge. Below this a lot cannot hold a room, so it becomes open ground.</summary>
		private const float MinBuildingEdge = CityConstants.Module * 2f;

		public static CityLayout Generate(int seed, CitySettings settings)
		{
			settings ??= CitySettings.CreateDefault();

			var layout = new CityLayout
			{
				Seed = seed,
				Bounds = settings.Area,
			};

			var rng = RunRng.ForRun(seed, RngStream.Layout);

			// A ring road around the whole city. Without it the outermost blocks dead-end into the map edge,
			// which reads as an unfinished level rather than a city boundary.
			Rect inner = Inset(layout.Bounds, settings.ArterialWidth);
			AddRing(layout, layout.Bounds, settings.ArterialWidth, RoadClass.Arterial);

			// 1 — Arterial split into districts.
			var districtRects = new List<Rect>(16);
			Subdivide(inner, settings.MaxDistrictSize, settings.MinDistrictSize, settings.ArterialWidth,
				RoadClass.Arterial, settings.SplitRange, ref rng, districtRects, layout.Roads, 0);

			// 2 — Give each district a character.
			AssignDistrictTypes(layout, districtRects, ref rng);

			// 3..5 — Street grid, alleys and lots, district by district.
			for (int d = 0; d < layout.Districts.Count; d++)
				BuildDistrict(layout, layout.Districts[d], settings, seed);

			layout.BuildIndex();
			return layout;
		}

		// =====================================================================
		// Districts
		// =====================================================================

		/// <summary>
		/// Assigns a <see cref="DistrictType"/> to every leaf rect. Type is driven by distance to the city centre
		/// so runs always read the same way at a glance — towers in the middle, works on the edge — while the mix
		/// in between still re-rolls. A city whose downtown moved every run would defeat the point: players are
		/// meant to learn the shape of a city and only re-learn the contents.
		/// </summary>
		private static void AssignDistrictTypes(CityLayout layout, List<Rect> rects, ref RunRng rng)
		{
			Vector2 center = layout.Bounds.center;
			float maxDist = new Vector2(layout.Bounds.width, layout.Bounds.height).magnitude * 0.5f;

			// Sort by distance to centre (ties broken by position so the order is deterministic).
			var order = new List<int>(rects.Count);
			for (int i = 0; i < rects.Count; i++) order.Add(i);
			order.Sort((a, b) =>
			{
				float da = (rects[a].center - center).sqrMagnitude;
				float db = (rects[b].center - center).sqrMagnitude;
				int cmp = da.CompareTo(db);
				if (cmp != 0) return cmp;
				cmp = rects[a].xMin.CompareTo(rects[b].xMin);
				return cmp != 0 ? cmp : rects[a].yMin.CompareTo(rects[b].yMin);
			});

			var assigned = new DistrictType[rects.Count];
			var done = new bool[rects.Count];

			// The core is always Downtown; bigger cities get a second tower district.
			int downtownCount = rects.Count >= 8 ? 2 : 1;
			for (int i = 0; i < downtownCount && i < order.Count; i++)
			{
				assigned[order[i]] = DistrictType.Downtown;
				done[order[i]] = true;
			}

			// Exactly one Civic district, just outside the core — it is the "hard locks, high value" district
			// and the run planner leans on there being exactly one of it.
			for (int i = downtownCount; i < order.Count; i++)
			{
				if (done[order[i]]) continue;
				assigned[order[i]] = DistrictType.Civic;
				done[order[i]] = true;
				break;
			}

			var weights = new float[4];
			for (int i = 0; i < order.Count; i++)
			{
				int idx = order[i];
				if (done[idx]) continue;

				float dist = maxDist > 0f ? (rects[idx].center - center).magnitude / maxDist : 0f;

				weights[0] = Mathf.Max(0f, 1f - dist * 1.6f);              // Business — hugs the core
				weights[1] = Mathf.Max(0.15f, 1f - Mathf.Abs(dist - 0.45f) * 2f); // Commercial — the middle ring
				weights[2] = Mathf.Clamp01(dist * 1.5f);                    // Residential — spreads outward
				weights[3] = Mathf.Clamp01((dist - 0.5f) * 2.2f);           // Industrial — the dark edge

				assigned[idx] = rng.PickWeighted(weights) switch
				{
					0 => DistrictType.Business,
					1 => DistrictType.Commercial,
					2 => DistrictType.Residential,
					_ => DistrictType.Industrial,
				};
			}

			for (int i = 0; i < rects.Count; i++)
			{
				var district = new CityDistrict
				{
					Index = layout.Districts.Count,
					Area = rects[i],
					Type = assigned[i],
				};
				var nameRng = rng.Fork(district.Index * 7919);
				district.Name = CityNames.District(ref nameRng, district.Type);
				layout.Districts.Add(district);
			}
		}

		/// <summary>Street grid, alleys and lots for one district.</summary>
		private static void BuildDistrict(CityLayout layout, CityDistrict district, CitySettings settings, int seed)
		{
			var profile = settings.Profile(district.Type);
			var rng = RunRng.For(seed, RngStream.Layout, district.Index, 1);

			var blockRects = new List<Rect>(16);
			Subdivide(district.Area, profile.TargetBlockSize, profile.TargetBlockSize * 0.55f,
				profile.StreetWidth, RoadClass.Street, settings.SplitRange, ref rng, blockRects, layout.Roads, 0);

			for (int b = 0; b < blockRects.Count; b++)
			{
				Rect blockArea = blockRects[b];

				// Long blocks get a service alley cut through the middle. Alleys are the covered ground route —
				// the thing that makes staying off the avenue possible at all.
				var pieces = new List<Rect>(2);
				if (Mathf.Max(blockArea.width, blockArea.height) > settings.AlleyBlockThreshold &&
					rng.Chance(0.7f))
				{
					SplitOnce(blockArea, settings.AlleyWidth, RoadClass.Alley, settings.SplitRange, ref rng,
						pieces, layout.Roads);
				}
				else
				{
					pieces.Add(blockArea);
				}

				for (int p = 0; p < pieces.Count; p++)
				{
					var block = new CityBlock
					{
						Index = layout.Blocks.Count,
						DistrictIndex = district.Index,
						Area = pieces[p],
					};
					layout.Blocks.Add(block);
					district.BlockIndices.Add(block.Index);

					SubdivideBlockIntoLots(layout, block, profile, settings, seed, ref rng);
				}
			}
		}

		// =====================================================================
		// Lots
		// =====================================================================

		/// <summary>
		/// Carves a block into a buildable perimeter band plus an open core. Each side of the band is then cut
		/// into street-fronting lots.
		/// </summary>
		private static void SubdivideBlockIntoLots(CityLayout layout, CityBlock block, DistrictProfile profile,
			CitySettings settings, int seed, ref RunRng rng)
		{
			Rect area = block.Area;
			float depth = profile.LotDepth;

			// Too thin for a ring — the whole block is one band, cut along its long axis.
			if (area.width <= depth * 2f + MinBuildingEdge || area.height <= depth * 2f + MinBuildingEdge)
			{
				bool alongX = area.width >= area.height;
				Vector2 frontage = alongX ? new Vector2(0f, -1f) : new Vector2(-1f, 0f);
				CutStrip(layout, block, area, alongX, frontage, profile, settings, seed, ref rng);
				return;
			}

			float x = area.xMin, y = area.yMin, w = area.width, h = area.height;

			// South and north bands run the full width; east and west bands fill what is left between them, so
			// the four never overlap at the corners.
			CutStrip(layout, block, new Rect(x, y, w, depth), true, new Vector2(0f, -1f), profile, settings, seed, ref rng);
			CutStrip(layout, block, new Rect(x, y + h - depth, w, depth), true, new Vector2(0f, 1f), profile, settings, seed, ref rng);
			CutStrip(layout, block, new Rect(x, y + depth, depth, h - depth * 2f), false, new Vector2(-1f, 0f), profile, settings, seed, ref rng);
			CutStrip(layout, block, new Rect(x + w - depth, y + depth, depth, h - depth * 2f), false, new Vector2(1f, 0f), profile, settings, seed, ref rng);

			// The core: an enclosed courtyard reachable only through the ring. Dark, covered and off the street.
			Rect core = new Rect(x + depth, y + depth, w - depth * 2f, h - depth * 2f);
			if (core.width >= MinBuildingEdge && core.height >= MinBuildingEdge)
			{
				var lot = new CityLot
				{
					Index = layout.Lots.Count,
					BlockIndex = block.Index,
					DistrictIndex = block.DistrictIndex,
					Area = core,
					Frontage = Vector2.down,
					Use = PickCourtyardUse(block.DistrictIndex >= 0 ? layout.Districts[block.DistrictIndex].Type : DistrictType.Commercial, ref rng),
				};
				layout.Lots.Add(lot);
				block.LotIndices.Add(lot.Index);
			}
		}

		/// <summary>Cuts one band of a block into lots along its long axis, then decides what each lot holds.</summary>
		private static void CutStrip(CityLayout layout, CityBlock block, Rect strip, bool alongX, Vector2 frontage,
			DistrictProfile profile, CitySettings settings, int seed, ref RunRng rng)
		{
			if (strip.width < MinBuildingEdge || strip.height < MinBuildingEdge) return;

			float length = alongX ? strip.width : strip.height;
			float avg = Mathf.Max(MinBuildingEdge, (profile.LotFrontage.x + profile.LotFrontage.y) * 0.5f);
			int count = Mathf.Max(1, Mathf.RoundToInt(length / avg));

			// Jittered weights normalised to the strip length. Dividing by weight rather than walking forward with
			// random widths means there is never a leftover sliver at the far end.
			var weights = new float[count];
			float total = 0f;
			for (int i = 0; i < count; i++)
			{
				weights[i] = rng.Range(0.78f, 1.22f);
				total += weights[i];
			}

			float cursor = alongX ? strip.xMin : strip.yMin;
			for (int i = 0; i < count; i++)
			{
				float size = length * (weights[i] / total);
				Rect lotArea = alongX
					? new Rect(cursor, strip.yMin, size, strip.height)
					: new Rect(strip.xMin, cursor, strip.width, size);
				cursor += size;

				var lot = new CityLot
				{
					Index = layout.Lots.Count,
					BlockIndex = block.Index,
					DistrictIndex = block.DistrictIndex,
					Area = lotArea,
					Frontage = frontage,
				};

				var lotRng = RunRng.For(seed, RngStream.Structure, lot.Index);
				AssignLotUse(layout, lot, profile, ref lotRng);

				layout.Lots.Add(lot);
				block.LotIndices.Add(lot.Index);
				if (lot.HasBuilding) layout.BuildingLots.Add(lot.Index);
			}
		}

		/// <summary>Decides whether a lot is built on and, if so, roughs out the building shell.</summary>
		private static void AssignLotUse(CityLayout layout, CityLot lot, DistrictProfile profile, ref RunRng rng)
		{
			Rect footprint = Inset(lot.Area, profile.Setback);

			// Anything too small to hold a room is open ground, whatever the dice said.
			if (footprint.width < MinBuildingEdge || footprint.height < MinBuildingEdge)
			{
				lot.Use = LotUse.Empty;
				return;
			}

			if (rng.Chance(profile.OpenLotChance))
			{
				lot.Use = PickOpenUse(profile.Type, ref rng);
				return;
			}

			lot.Use = LotUse.Building;
			lot.Building = CreateBuildingShell(layout, lot, footprint, profile, ref rng);
			if (lot.Building == null) lot.Use = LotUse.Empty;
		}

		private static LotUse PickOpenUse(DistrictType type, ref RunRng rng)
		{
			return type switch
			{
				DistrictType.Industrial  => rng.Chance(0.6f) ? LotUse.Yard : LotUse.ParkingLot,
				DistrictType.Downtown    => rng.Chance(0.5f) ? LotUse.Plaza : LotUse.ParkingLot,
				DistrictType.Civic       => rng.Chance(0.6f) ? LotUse.Plaza : LotUse.Park,
				DistrictType.Residential => rng.Chance(0.5f) ? LotUse.Park : LotUse.ParkingLot,
				_                        => rng.Chance(0.25f) ? LotUse.ConstructionSite : LotUse.ParkingLot,
			};
		}

		private static LotUse PickCourtyardUse(DistrictType type, ref RunRng rng)
		{
			return type switch
			{
				DistrictType.Industrial  => LotUse.Yard,
				DistrictType.Residential => rng.Chance(0.5f) ? LotUse.Park : LotUse.ParkingLot,
				DistrictType.Downtown    => rng.Chance(0.4f) ? LotUse.Plaza : LotUse.ParkingLot,
				_                        => LotUse.ParkingLot,
			};
		}

		// =====================================================================
		// Building shells
		// =====================================================================

		/// <summary>
		/// Roughs out a building: archetype, module-snapped footprint, storey count, roof access and fire escapes.
		/// The interior (rooms, corridors, doors) is deliberately <em>not</em> generated here — see
		/// <see cref="BuildingPlanGenerator"/>. A 1&#160;km city holds well over a thousand buildings, and
		/// partitioning every floor of every one of them at match start would cost seconds of load for interiors
		/// nobody will ever open.
		/// </summary>
		private static BuildingPlan CreateBuildingShell(CityLayout layout, CityLot lot, Rect footprint,
			DistrictProfile profile, ref RunRng rng)
		{
			// Snap the footprint to whole modules so kit pieces tile exactly, then re-centre it in the lot.
			int gx = Mathf.FloorToInt(footprint.width / CityConstants.Module);
			int gz = Mathf.FloorToInt(footprint.height / CityConstants.Module);
			if (gx < 2 || gz < 2) return null;

			float snappedW = gx * CityConstants.Module;
			float snappedH = gz * CityConstants.Module;
			var snapped = new Rect(
				footprint.center.x - snappedW * 0.5f,
				footprint.center.y - snappedH * 0.5f,
				snappedW, snappedH);

			var archetype = PickArchetype(profile, snapped, ref rng);

			var plan = new BuildingPlan
			{
				LotIndex = lot.Index,
				Archetype = archetype,
				Footprint = snapped,
				GridSize = new Vector2Int(gx, gz),
				BaseY = 0f,
				FloorHeight = archetype == BuildingArchetype.Warehouse ? 6.5f : CityConstants.FloorHeight,
			};

			plan.Name = BuildName(ref rng, archetype);

			int floors = rng.Range(profile.Floors.x, profile.Floors.y + 1);
			plan.PlannedFloors = Mathf.Max(1, ClampFloorsForArchetype(archetype, floors));

			// Roof access is what makes a building part of the rooftop network. Tall buildings almost always have
			// it (fire code); low retail rarely does.
			plan.HasRoofAccess = plan.PlannedFloors >= 2 && rng.Chance(profile.RoofAccessChance);
			plan.ParapetHeight = archetype == BuildingArchetype.ParkingGarage ? 1.4f : rng.Range(0.9f, 1.3f);

			if (plan.PlannedFloors >= 2 && rng.Chance(profile.FireEscapeChance))
			{
				// Fire escapes hang off the back — the side away from the street the lot fronts onto.
				int backSide = SideFromDirection(-lot.Frontage);
				plan.FireEscapeSides.Add(backSide);
				if (rng.Chance(0.25f)) plan.FireEscapeSides.Add((backSide + 1) & 3);
			}

			return plan;
		}

		private static string BuildName(ref RunRng rng, BuildingArchetype archetype)
		{
			var nameRng = rng.Fork(31);
			return CityNames.Building(ref nameRng, archetype);
		}

		/// <summary>Weighted archetype pick, filtered to those that physically fit the footprint. Falls back to a
		/// shop (the smallest archetype) when nothing else fits.</summary>
		private static BuildingArchetype PickArchetype(DistrictProfile profile, Rect footprint, ref RunRng rng)
		{
			int count = System.Enum.GetValues(typeof(BuildingArchetype)).Length;
			var weights = new float[count];

			for (int i = 0; i < count; i++)
			{
				float w = profile.ArchetypeWeights != null && i < profile.ArchetypeWeights.Length
					? profile.ArchetypeWeights[i]
					: 0f;
				if (w > 0f && !ArchetypeFits((BuildingArchetype)i, footprint)) w = 0f;
				weights[i] = w;
			}

			int pick = rng.PickWeighted(weights);
			if (pick >= 0) return (BuildingArchetype)pick;

			return ArchetypeFits(BuildingArchetype.Shop, footprint)
				? BuildingArchetype.Shop
				: BuildingArchetype.Utility;
		}

		/// <summary>Minimum footprint each archetype needs to make sense. A warehouse in a 10&#160;m lot would be a
		/// shed with a warehouse's loot table, so size gates the choice rather than the label lying.</summary>
		private static bool ArchetypeFits(BuildingArchetype archetype, Rect footprint)
		{
			float min = Mathf.Min(footprint.width, footprint.height);
			float max = Mathf.Max(footprint.width, footprint.height);

			return archetype switch
			{
				BuildingArchetype.HighRiseOffice => min >= 22f,
				BuildingArchetype.OfficeBlock    => min >= 14f,
				BuildingArchetype.Supermarket    => min >= 24f && max >= 30f,
				BuildingArchetype.Warehouse      => min >= 24f && max >= 32f,
				BuildingArchetype.ParkingGarage  => min >= 22f,
				BuildingArchetype.CivicHall      => min >= 20f,
				BuildingArchetype.GasStation     => min >= 10f && max <= 40f,
				BuildingArchetype.ApartmentBlock => min >= 12f,
				BuildingArchetype.Shop           => min >= MinBuildingEdge,
				BuildingArchetype.Utility        => min >= MinBuildingEdge,
				_                                => true,
			};
		}

		private static int ClampFloorsForArchetype(BuildingArchetype archetype, int floors)
		{
			return archetype switch
			{
				BuildingArchetype.HighRiseOffice => Mathf.Max(8, floors),
				BuildingArchetype.GasStation     => 1,
				BuildingArchetype.Warehouse      => Mathf.Clamp(floors, 1, 2),
				BuildingArchetype.Supermarket    => Mathf.Clamp(floors, 1, 2),
				BuildingArchetype.Shop           => Mathf.Clamp(floors, 1, 3),
				BuildingArchetype.ParkingGarage  => Mathf.Clamp(floors, 3, 7),
				BuildingArchetype.Utility        => 1,
				BuildingArchetype.CivicHall      => Mathf.Clamp(floors, 2, 8),
				_                                => Mathf.Max(1, floors),
			};
		}

		/// <summary>Facade side index (0=-Z, 1=+X, 2=+Z, 3=-X) closest to a direction.</summary>
		private static int SideFromDirection(Vector2 dir)
		{
			if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)) return dir.x >= 0f ? 1 : 3;
			return dir.y >= 0f ? 2 : 0;
		}

		// =====================================================================
		// BSP primitives
		// =====================================================================

		/// <summary>
		/// Recursively splits <paramref name="area"/> until every leaf is at most <paramref name="maxSize"/> on
		/// both axes, inserting a road of <paramref name="roadWidth"/> at each cut. Leaves are appended to
		/// <paramref name="output"/>.
		/// </summary>
		private static void Subdivide(Rect area, float maxSize, float minSize, float roadWidth, RoadClass cls,
			Vector2 splitRange, ref RunRng rng, List<Rect> output, List<RoadSegment> roads, int depth)
		{
			bool tooWide = area.width > maxSize;
			bool tooTall = area.height > maxSize;

			if (depth >= MaxDepth || (!tooWide && !tooTall))
			{
				output.Add(area);
				return;
			}

			// Split the axis that is over budget; when both are, split the longer one so leaves stay squarish.
			bool splitX = tooWide && (!tooTall || area.width >= area.height);
			float span = splitX ? area.width : area.height;

			// Not enough room for two viable children plus the road — stop here rather than produce slivers.
			if (span < minSize * 2f + roadWidth)
			{
				output.Add(area);
				return;
			}

			SplitAxis(area, splitX, roadWidth, cls, splitRange, minSize, ref rng, out Rect a, out Rect b, roads);
			Subdivide(a, maxSize, minSize, roadWidth, cls, splitRange, ref rng, output, roads, depth + 1);
			Subdivide(b, maxSize, minSize, roadWidth, cls, splitRange, ref rng, output, roads, depth + 1);
		}

		/// <summary>Single split of a rect along its longer axis. Used for alleys, which cut a block exactly once.</summary>
		private static void SplitOnce(Rect area, float roadWidth, RoadClass cls, Vector2 splitRange,
			ref RunRng rng, List<Rect> output, List<RoadSegment> roads)
		{
			bool splitX = area.width >= area.height;
			float span = splitX ? area.width : area.height;
			if (span < roadWidth + MinBuildingEdge * 2f)
			{
				output.Add(area);
				return;
			}

			SplitAxis(area, splitX, roadWidth, cls, splitRange, MinBuildingEdge, ref rng, out Rect a, out Rect b, roads);
			output.Add(a);
			output.Add(b);
		}

		private static void SplitAxis(Rect area, bool splitX, float roadWidth, RoadClass cls, Vector2 splitRange,
			float minSide, ref RunRng rng, out Rect a, out Rect b, List<RoadSegment> roads)
		{
			float span = splitX ? area.width : area.height;
			float origin = splitX ? area.xMin : area.yMin;

			float t = rng.Range(Mathf.Min(splitRange.x, splitRange.y), Mathf.Max(splitRange.x, splitRange.y));
			float cut = origin + span * t;

			// Keep both children at least minSide wide after the road is carved out.
			float lo = origin + minSide + roadWidth * 0.5f;
			float hi = origin + span - minSide - roadWidth * 0.5f;
			cut = Mathf.Clamp(cut, lo, hi);

			float roadMin = cut - roadWidth * 0.5f;
			float roadMax = cut + roadWidth * 0.5f;

			if (splitX)
			{
				a = new Rect(area.xMin, area.yMin, roadMin - area.xMin, area.height);
				b = new Rect(roadMax, area.yMin, area.xMax - roadMax, area.height);
				roads.Add(new RoadSegment
				{
					Area = new Rect(roadMin, area.yMin, roadWidth, area.height),
					Class = cls,
					Horizontal = false,
				});
			}
			else
			{
				a = new Rect(area.xMin, area.yMin, area.width, roadMin - area.yMin);
				b = new Rect(area.xMin, roadMax, area.width, area.yMax - roadMax);
				roads.Add(new RoadSegment
				{
					Area = new Rect(area.xMin, roadMin, area.width, roadWidth),
					Class = cls,
					Horizontal = true,
				});
			}
		}

		/// <summary>Adds the four segments of a ring road around <paramref name="outer"/>.</summary>
		private static void AddRing(CityLayout layout, Rect outer, float width, RoadClass cls)
		{
			layout.Roads.Add(new RoadSegment { Area = new Rect(outer.xMin, outer.yMin, outer.width, width), Class = cls, Horizontal = true });
			layout.Roads.Add(new RoadSegment { Area = new Rect(outer.xMin, outer.yMax - width, outer.width, width), Class = cls, Horizontal = true });
			layout.Roads.Add(new RoadSegment { Area = new Rect(outer.xMin, outer.yMin, width, outer.height), Class = cls, Horizontal = false });
			layout.Roads.Add(new RoadSegment { Area = new Rect(outer.xMax - width, outer.yMin, width, outer.height), Class = cls, Horizontal = false });
		}

		private static Rect Inset(Rect r, float amount) =>
			new Rect(r.xMin + amount, r.yMin + amount,
				Mathf.Max(0f, r.width - amount * 2f), Mathf.Max(0f, r.height - amount * 2f));
	}
}
