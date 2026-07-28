using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// One drivable surface. Rects are axis-aligned in the XZ plane (Rect.x/y map to world X/Z), which keeps
	/// every layout query to cheap rectangle maths. The city is a Manhattan grid on purpose: it makes street
	/// visibility, rooftop gaps and "which way is the alley" legible to players at a glance, and it makes the
	/// generator's overlap tests exact instead of approximate.
	/// </summary>
	public sealed class RoadSegment
	{
		public Rect Area;
		public RoadClass Class;
		/// <summary>True when the segment runs along world X (its width is the long axis).</summary>
		public bool Horizontal;

		public float Width => Horizontal ? Area.height : Area.width;
		public Vector3 Center => new Vector3(Area.center.x, 0f, Area.center.y);
	}

	/// <summary>
	/// The smallest unit of land. A lot either hosts one building or is an open space. Lots are the atoms the
	/// run planner reasons about: "the objective is in lot 412" is what a clue ultimately resolves to.
	/// </summary>
	public sealed class CityLot
	{
		public int Index;
		public int BlockIndex;
		public int DistrictIndex;

		public Rect Area;
		public LotUse Use;

		/// <summary>Direction from the lot centre toward the street it fronts onto. Entrances face this way.</summary>
		public Vector2 Frontage = Vector2.down;

		/// <summary>Null unless <see cref="Use"/> is <see cref="LotUse.Building"/>.</summary>
		public BuildingPlan Building;

		public Vector3 Center => new Vector3(Area.center.x, 0f, Area.center.y);
		public bool HasBuilding => Use == LotUse.Building && Building != null;
	}

	/// <summary>A city block — the land enclosed by streets, subdivided into lots (and possibly split by an alley).</summary>
	public sealed class CityBlock
	{
		public int Index;
		public int DistrictIndex;
		public Rect Area;
		public readonly List<int> LotIndices = new List<int>(8);
		public Vector3 Center => new Vector3(Area.center.x, 0f, Area.center.y);
	}

	/// <summary>A named neighbourhood. Districts are the unit navigation terminals reveal and the unit clues
	/// name ("somewhere in the Harrow Street market district"), so they need to be few, large and memorable.</summary>
	public sealed class CityDistrict
	{
		public int Index;
		public Rect Area;
		public DistrictType Type;
		public string Name;
		public readonly List<int> BlockIndices = new List<int>(16);
		public Vector3 Center => new Vector3(Area.center.x, 0f, Area.center.y);
	}

	/// <summary>
	/// The complete, purely-logical description of a run's city: no GameObjects, no meshes, no Unity lifecycle.
	///
	/// <para>This separation is deliberate and load-bearing. The layout is small (a few hundred KB of plain C#)
	/// and fully determined by the run seed, so every peer computes an identical copy locally at match start and
	/// <b>no geometry is ever replicated</b>. <see cref="CityBuilder"/> then turns whatever slice of it is nearby
	/// into actual meshes. It also means the run planner can solve the whole 1&#215;1&#160;km logic graph in
	/// milliseconds before a single wall exists.</para>
	/// </summary>
	public sealed class CityLayout
	{
		/// <summary>Run seed this layout was generated from.</summary>
		public int Seed;

		/// <summary>World-space footprint of the city (XZ). Default 1000&#215;1000&#160;m.</summary>
		public Rect Bounds;

		public readonly List<CityDistrict> Districts = new List<CityDistrict>(16);
		public readonly List<CityBlock> Blocks = new List<CityBlock>(128);
		public readonly List<CityLot> Lots = new List<CityLot>(1024);
		public readonly List<RoadSegment> Roads = new List<RoadSegment>(256);

		/// <summary>Indices into <see cref="Lots"/> that carry a building, cached for the many passes that only
		/// care about buildings (objective placement, streaming, nav).</summary>
		public readonly List<int> BuildingLots = new List<int>(512);

		// ---- Uniform spatial index -------------------------------------------------
		// A flat grid over Bounds. Lot lookups happen constantly (streaming, "which building am I in",
		// monster routing), and a 1 km city has enough lots that a linear scan per query would show up
		// in a profile. Cell size is chosen so a cell holds a handful of lots.
		private const float IndexCellSize = 50f;
		private List<int>[] _lotIndex;
		private int _indexCols, _indexRows;

		public CityDistrict DistrictOfLot(CityLot lot) =>
			lot != null && lot.DistrictIndex >= 0 && lot.DistrictIndex < Districts.Count ? Districts[lot.DistrictIndex] : null;

		/// <summary>Build the spatial index. Called once by the generator when the layout is complete.</summary>
		public void BuildIndex()
		{
			_indexCols = Mathf.Max(1, Mathf.CeilToInt(Bounds.width / IndexCellSize));
			_indexRows = Mathf.Max(1, Mathf.CeilToInt(Bounds.height / IndexCellSize));
			_lotIndex = new List<int>[_indexCols * _indexRows];

			for (int i = 0; i < Lots.Count; i++)
			{
				var lot = Lots[i];
				GetCellRange(lot.Area, out int x0, out int z0, out int x1, out int z1);
				for (int z = z0; z <= z1; z++)
				for (int x = x0; x <= x1; x++)
				{
					int cell = z * _indexCols + x;
					(_lotIndex[cell] ??= new List<int>(4)).Add(i);
				}
			}
		}

		/// <summary>Lot containing <paramref name="worldPos"/>, or null when the position is on a road / outside
		/// the city. Cheap enough to call per frame.</summary>
		public CityLot LotAt(Vector3 worldPos)
		{
			if (_lotIndex == null) return null;

			var p = new Vector2(worldPos.x, worldPos.z);
			int cell = CellOf(p);
			if (cell < 0) return null;

			var bucket = _lotIndex[cell];
			if (bucket == null) return null;

			for (int i = 0; i < bucket.Count; i++)
			{
				var lot = Lots[bucket[i]];
				if (lot.Area.Contains(p)) return lot;
			}
			return null;
		}

		/// <summary>Every lot whose area intersects <paramref name="area"/>. Results are appended to
		/// <paramref name="results"/> (which is cleared first) so callers can reuse a buffer.</summary>
		public void LotsOverlapping(Rect area, List<CityLot> results)
		{
			results.Clear();
			if (_lotIndex == null) return;

			GetCellRange(area, out int x0, out int z0, out int x1, out int z1);
			for (int z = z0; z <= z1; z++)
			for (int x = x0; x <= x1; x++)
			{
				var bucket = _lotIndex[z * _indexCols + x];
				if (bucket == null) continue;

				for (int i = 0; i < bucket.Count; i++)
				{
					var lot = Lots[bucket[i]];
					if (!lot.Area.Overlaps(area)) continue;
					if (!results.Contains(lot)) results.Add(lot);
				}
			}
		}

		/// <summary>Building lots within <paramref name="radius"/> of <paramref name="worldPos"/>. Used by the
		/// streamer to decide what to build.</summary>
		public void BuildingLotsNear(Vector3 worldPos, float radius, List<CityLot> results)
		{
			var area = new Rect(worldPos.x - radius, worldPos.z - radius, radius * 2f, radius * 2f);
			LotsOverlapping(area, results);
			for (int i = results.Count - 1; i >= 0; i--)
				if (!results[i].HasBuilding) results.RemoveAt(i);
		}

		private int CellOf(Vector2 p)
		{
			int x = Mathf.FloorToInt((p.x - Bounds.xMin) / IndexCellSize);
			int z = Mathf.FloorToInt((p.y - Bounds.yMin) / IndexCellSize);
			if (x < 0 || z < 0 || x >= _indexCols || z >= _indexRows) return -1;
			return z * _indexCols + x;
		}

		private void GetCellRange(Rect area, out int x0, out int z0, out int x1, out int z1)
		{
			x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - Bounds.xMin) / IndexCellSize), 0, _indexCols - 1);
			x1 = Mathf.Clamp(Mathf.FloorToInt((area.xMax - Bounds.xMin) / IndexCellSize), 0, _indexCols - 1);
			z0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - Bounds.yMin) / IndexCellSize), 0, _indexRows - 1);
			z1 = Mathf.Clamp(Mathf.FloorToInt((area.yMax - Bounds.yMin) / IndexCellSize), 0, _indexRows - 1);
		}

		/// <summary>Human-readable address for a lot — "Meridian Tower, Harrow District". This is the string a
		/// clue hands the player, so it must be stable for a given seed and unique enough to act on.</summary>
		public string AddressOf(CityLot lot)
		{
			if (lot == null) return "unknown";
			var district = DistrictOfLot(lot);
			string districtName = district != null ? district.Name : "the city";
			if (lot.HasBuilding && !string.IsNullOrEmpty(lot.Building.Name))
				return $"{lot.Building.Name}, {districtName}";
			return districtName;
		}
	}
}
