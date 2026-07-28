using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Owns a procedurally built <see cref="Mesh"/> and destroys it with its GameObject.
	///
	/// <para>Meshes created at runtime are unmanaged native allocations. Destroying the GameObject that renders
	/// one frees the components but <b>not</b> the mesh — it stays resident until the scene unloads. That is
	/// invisible in a normal scene and fatal here: the streamer builds and tears down building interiors
	/// continuously as players cross the city, so every leaked mesh is permanent growth for the whole match.
	/// A player walking a few blocks would strand hundreds of megabytes.</para>
	///
	/// <para>Attached by <see cref="CityBuilder"/> to everything it bakes. Deliberately <em>not</em> attached to
	/// building shells: those share one static unit cube, and destroying it would break every other shell.</para>
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class RuntimeMesh : MonoBehaviour
	{
		[Tooltip("The generated mesh this object owns. Destroyed alongside the GameObject.")]
		public Mesh Mesh;

		private void OnDestroy()
		{
			if (Mesh == null) return;

			if (Application.isPlaying) Destroy(Mesh);
			else                       DestroyImmediate(Mesh);

			Mesh = null;
		}
	}
}
