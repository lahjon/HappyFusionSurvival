using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// A walkable graph over the city's streets, built directly from the road layout.
	///
	/// <para><b>Why not a NavMesh.</b> The city is generated at runtime and streams in and out; baking a NavMesh
	/// over a square kilometre of geometry that does not exist yet is not an option, and runtime NavMesh building
	/// would have to re-bake every time a building is realised. But the generator already knows exactly where the
	/// roads are — the walkable space is not something to be discovered, it is an input. Sampling it directly is
	/// both cheaper and more accurate than recovering it from geometry.</para>
	///
	/// <para>The graph covers streets, alleys and open lots only. Monsters use it to cross the city; once they
	/// have a player in sight they steer directly, and inside buildings they follow the room portal graph. Three
	/// cheap systems instead of one expensive general one.</para>
	/// </summary>
	public sealed class CityNavGraph
	{
		/// <summary>Distance between sampled nodes along a road centreline.</summary>
		private const float NodeSpacing = 18f;

		/// <summary>Nodes closer than this are linked. Slightly above <see cref="NodeSpacing"/> so junctions —
		/// where two roads' samples land near each other — connect without an explicit intersection pass.</summary>
		private const float LinkRadius = 26f;

		private const float IndexCellSize = 40f;

		private readonly List<Vector3> _positions = new List<Vector3>(2048);
		private readonly List<List<int>> _links = new List<List<int>>(2048);
		private Dictionary<int, List<int>> _index;
		private int _cols, _rows;
		private Rect _bounds;

		public int NodeCount => _positions.Count;
		public Vector3 Position(int node) => _positions[node];

		// =====================================================================

		public static CityNavGraph Build(CityLayout layout)
		{
			var graph = new CityNavGraph { _bounds = layout.Bounds };

			for (int i = 0; i < layout.Roads.Count; i++)
			{
				var road = layout.Roads[i];
				var area = road.Area;

				float length = road.Horizontal ? area.width : area.height;
				int steps = Mathf.Max(2, Mathf.CeilToInt(length / NodeSpacing));

				for (int s = 0; s <= steps; s++)
				{
					float t = s / (float)steps;
					Vector3 p = road.Horizontal
						? new Vector3(Mathf.Lerp(area.xMin, area.xMax, t), 0f, area.center.y)
						: new Vector3(area.center.x, 0f, Mathf.Lerp(area.yMin, area.yMax, t));
					graph.AddNode(p);
				}
			}

			// Open lots are walkable too, and they are where players get caught in the open — monsters need to be
			// able to cut across them rather than politely following the kerb.
			for (int i = 0; i < layout.Lots.Count; i++)
			{
				var lot = layout.Lots[i];
				if (lot.Use is LotUse.Building or LotUse.Empty) continue;
				graph.AddNode(lot.Center);
			}

			graph.BuildIndex();
			graph.LinkNodes();
			return graph;
		}

		private void AddNode(Vector3 position)
		{
			_positions.Add(position);
			_links.Add(new List<int>(4));
		}

		private void BuildIndex()
		{
			_cols = Mathf.Max(1, Mathf.CeilToInt(_bounds.width / IndexCellSize));
			_rows = Mathf.Max(1, Mathf.CeilToInt(_bounds.height / IndexCellSize));
			_index = new Dictionary<int, List<int>>(_cols * _rows);

			for (int i = 0; i < _positions.Count; i++)
			{
				int cell = CellOf(_positions[i]);
				if (cell < 0) continue;
				if (!_index.TryGetValue(cell, out var bucket))
					_index[cell] = bucket = new List<int>(8);
				bucket.Add(i);
			}
		}

		private void LinkNodes()
		{
			var nearby = new List<int>(32);
			float sqrRadius = LinkRadius * LinkRadius;

			for (int i = 0; i < _positions.Count; i++)
			{
				Gather(_positions[i], LinkRadius, nearby);
				for (int n = 0; n < nearby.Count; n++)
				{
					int j = nearby[n];
					if (j <= i) continue;
					if ((_positions[i] - _positions[j]).sqrMagnitude > sqrRadius) continue;

					_links[i].Add(j);
					_links[j].Add(i);
				}
			}
		}

		private int CellOf(Vector3 p)
		{
			int x = Mathf.FloorToInt((p.x - _bounds.xMin) / IndexCellSize);
			int z = Mathf.FloorToInt((p.z - _bounds.yMin) / IndexCellSize);
			if (x < 0 || z < 0 || x >= _cols || z >= _rows) return -1;
			return z * _cols + x;
		}

		private void Gather(Vector3 center, float radius, List<int> results)
		{
			results.Clear();
			if (_index == null) return;

			int reach = Mathf.CeilToInt(radius / IndexCellSize);
			int cx = Mathf.FloorToInt((center.x - _bounds.xMin) / IndexCellSize);
			int cz = Mathf.FloorToInt((center.z - _bounds.yMin) / IndexCellSize);

			for (int z = cz - reach; z <= cz + reach; z++)
			for (int x = cx - reach; x <= cx + reach; x++)
			{
				if (x < 0 || z < 0 || x >= _cols || z >= _rows) continue;
				if (_index.TryGetValue(z * _cols + x, out var bucket))
					results.AddRange(bucket);
			}
		}

		// =====================================================================

		/// <summary>Nearest graph node to a world position, or -1 if the graph is empty.</summary>
		public int NearestNode(Vector3 position)
		{
			var nearby = new List<int>(32);
			Gather(position, IndexCellSize * 2f, nearby);
			if (nearby.Count == 0)
			{
				// Far from any road (deep inside a block). Fall back to a full scan — rare, and correctness
				// matters more than speed on the exceptional path.
				int best = -1;
				float bestSqr = float.MaxValue;
				for (int i = 0; i < _positions.Count; i++)
				{
					float d = (_positions[i] - position).sqrMagnitude;
					if (d >= bestSqr) continue;
					bestSqr = d;
					best = i;
				}
				return best;
			}

			int nearest = -1;
			float nearestSqr = float.MaxValue;
			for (int i = 0; i < nearby.Count; i++)
			{
				float d = (_positions[nearby[i]] - position).sqrMagnitude;
				if (d >= nearestSqr) continue;
				nearestSqr = d;
				nearest = nearby[i];
			}
			return nearest;
		}

		/// <summary>A random node within <paramref name="radius"/> of a position, for patrol wandering.</summary>
		public int RandomNodeNear(Vector3 position, float radius, ref RunRng rng)
		{
			var nearby = new List<int>(64);
			Gather(position, radius, nearby);
			if (nearby.Count == 0) return NearestNode(position);
			return nearby[rng.Index(nearby.Count)];
		}

		/// <summary>
		/// A* between two nodes. Appends world positions to <paramref name="path"/> (cleared first) and returns
		/// false when no route exists. Allocates its working set per call — monsters repath on the order of once
		/// every few seconds, not every frame, so this is not a hot path.
		/// </summary>
		public bool TryFindPath(int from, int to, List<Vector3> path)
		{
			path.Clear();
			if (from < 0 || to < 0 || from >= _positions.Count || to >= _positions.Count) return false;
			if (from == to)
			{
				path.Add(_positions[to]);
				return true;
			}

			int count = _positions.Count;
			var cameFrom = new int[count];
			var gScore = new float[count];
			var closed = new bool[count];
			for (int i = 0; i < count; i++) { cameFrom[i] = -1; gScore[i] = float.MaxValue; }

			gScore[from] = 0f;

			// Simple binary heap keyed on f-score.
			var open = new List<(int node, float f)>(64) { (from, Heuristic(from, to)) };

			while (open.Count > 0)
			{
				int bestIndex = 0;
				for (int i = 1; i < open.Count; i++)
					if (open[i].f < open[bestIndex].f) bestIndex = i;

				int current = open[bestIndex].node;
				open.RemoveAt(bestIndex);

				if (current == to)
				{
					Reconstruct(cameFrom, current, path);
					return true;
				}

				if (closed[current]) continue;
				closed[current] = true;

				var neighbours = _links[current];
				for (int n = 0; n < neighbours.Count; n++)
				{
					int next = neighbours[n];
					if (closed[next]) continue;

					float tentative = gScore[current] + Vector3.Distance(_positions[current], _positions[next]);
					if (tentative >= gScore[next]) continue;

					cameFrom[next] = current;
					gScore[next] = tentative;
					open.Add((next, tentative + Heuristic(next, to)));
				}
			}

			return false;
		}

		private float Heuristic(int a, int b) => Vector3.Distance(_positions[a], _positions[b]);

		private void Reconstruct(int[] cameFrom, int current, List<Vector3> path)
		{
			var reversed = new List<int>(32);
			while (current >= 0)
			{
				reversed.Add(current);
				current = cameFrom[current];
			}
			for (int i = reversed.Count - 1; i >= 0; i--)
				path.Add(_positions[reversed[i]]);
		}
	}
}
