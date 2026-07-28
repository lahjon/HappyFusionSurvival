using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Minimal dynamic mesh assembler for the city's procedural geometry.
	///
	/// <para>The city is built from boxes and quads: road surfaces, kerbs, building massing, walls, floors,
	/// parapets. Instantiating a prefab per piece would mean tens of thousands of GameObjects for a square
	/// kilometre; batching them into one mesh per block (or per building) keeps the draw call count in the
	/// hundreds and the transform hierarchy shallow enough that streaming a chunk in or out is cheap.</para>
	///
	/// <para>Buffers are reused between builds via <see cref="Clear"/>, so streaming does not churn the GC.</para>
	/// </summary>
	public sealed class MeshBuilder
	{
		private readonly List<Vector3> _vertices = new List<Vector3>(1024);
		private readonly List<Vector3> _normals = new List<Vector3>(1024);
		private readonly List<Vector2> _uvs = new List<Vector2>(1024);
		private readonly List<int> _triangles = new List<int>(2048);

		public int VertexCount => _vertices.Count;
		public bool IsEmpty => _triangles.Count == 0;

		public void Clear()
		{
			_vertices.Clear();
			_normals.Clear();
			_uvs.Clear();
			_triangles.Clear();
		}

		/// <summary>Axis-aligned box from <paramref name="min"/> to <paramref name="max"/>. UVs are world-scaled so
		/// a tiling material reads at a consistent density regardless of the box's size.</summary>
		public void AddBox(Vector3 min, Vector3 max, float uvScale = 0.25f)
		{
			// -Z / +Z
			AddQuad(new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z),
					new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), Vector3.back, uvScale);
			AddQuad(new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
					new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), Vector3.forward, uvScale);
			// -X / +X
			AddQuad(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
					new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), Vector3.left, uvScale);
			AddQuad(new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z),
					new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), Vector3.right, uvScale);
			// -Y / +Y
			AddQuad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
					new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), Vector3.down, uvScale);
			AddQuad(new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z),
					new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), Vector3.up, uvScale);
		}

		/// <summary>Flat horizontal quad at height <paramref name="y"/> covering <paramref name="area"/> (XZ).</summary>
		public void AddHorizontalQuad(Rect area, float y, float uvScale = 0.1f)
		{
			AddQuad(
				new Vector3(area.xMin, y, area.yMin),
				new Vector3(area.xMax, y, area.yMin),
				new Vector3(area.xMax, y, area.yMax),
				new Vector3(area.xMin, y, area.yMax),
				Vector3.up, uvScale);
		}

		/// <summary>Counter-clockwise quad with a flat normal.</summary>
		public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float uvScale)
		{
			int baseIndex = _vertices.Count;

			_vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
			for (int i = 0; i < 4; i++) _normals.Add(normal);

			// Project onto whichever plane the normal is least aligned with, so UVs never collapse.
			Vector3 uAxis, vAxis;
			if (Mathf.Abs(normal.y) > 0.7f) { uAxis = Vector3.right; vAxis = Vector3.forward; }
			else if (Mathf.Abs(normal.x) > 0.7f) { uAxis = Vector3.forward; vAxis = Vector3.up; }
			else { uAxis = Vector3.right; vAxis = Vector3.up; }

			_uvs.Add(new Vector2(Vector3.Dot(a, uAxis), Vector3.Dot(a, vAxis)) * uvScale);
			_uvs.Add(new Vector2(Vector3.Dot(b, uAxis), Vector3.Dot(b, vAxis)) * uvScale);
			_uvs.Add(new Vector2(Vector3.Dot(c, uAxis), Vector3.Dot(c, vAxis)) * uvScale);
			_uvs.Add(new Vector2(Vector3.Dot(d, uAxis), Vector3.Dot(d, vAxis)) * uvScale);

			_triangles.Add(baseIndex); _triangles.Add(baseIndex + 2); _triangles.Add(baseIndex + 1);
			_triangles.Add(baseIndex); _triangles.Add(baseIndex + 3); _triangles.Add(baseIndex + 2);
		}

		/// <summary>
		/// Bake the accumulated geometry. Returns null when nothing was added, so callers can skip creating an
		/// empty renderer. 32-bit indices because a whole city block's massing can exceed 65k vertices.
		/// </summary>
		public Mesh ToMesh(string name)
		{
			if (IsEmpty) return null;

			var mesh = new Mesh
			{
				name = name,
				indexFormat = _vertices.Count > 65000
					? UnityEngine.Rendering.IndexFormat.UInt32
					: UnityEngine.Rendering.IndexFormat.UInt16,
			};

			mesh.SetVertices(_vertices);
			mesh.SetNormals(_normals);
			mesh.SetUVs(0, _uvs);
			mesh.SetTriangles(_triangles, 0);
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
