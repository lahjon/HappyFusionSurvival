using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Distance-culls the city's building lights (entrance lamps, interior fixtures) against the local camera.
	///
	/// <para>The static city realises every interior up front, which puts hundreds of Light components in the
	/// scene at once — far past what URP will tolerate live. Same answer as <see cref="StreetLightGrid"/> uses
	/// for the street lamps: every fixture keeps its emissive head (so lit windows read from across the town),
	/// but only the Lights near the local camera actually illuminate. Purely local and cosmetic — which lights
	/// are enabled never touches gameplay or networking, so each peer culls independently.</para>
	///
	/// <para>Registration is a static call so <see cref="CityBuilder"/> (a static class) can hand lights over
	/// without owning a component reference; the singleton bootstraps itself on first use.</para>
	/// </summary>
	public sealed class CityLightCuller : MonoBehaviour
	{
		[Tooltip("Lights within this distance of the local camera are live.")]
		[Min(5f)] public float ActiveRadius = 55f;

		[Tooltip("Seconds between culling passes.")]
		[Min(0.05f)] public float CullInterval = 0.3f;

		private static CityLightCuller _instance;

		private readonly List<Light> _lights = new List<Light>(512);
		private readonly List<Vector3> _positions = new List<Vector3>(512);
		private float _nextCull;

		/// <summary>Register a building light for culling. Creates the culler on first use. The light starts
		/// disabled and is enabled by proximity.</summary>
		public static void Register(Light light)
		{
			if (light == null) return;

			if (_instance == null)
			{
				var go = new GameObject("CityLightCuller");
				_instance = go.AddComponent<CityLightCuller>();
			}

			light.enabled = false;
			_instance._lights.Add(light);
			_instance._positions.Add(light.transform.position);
		}

		private void OnDestroy()
		{
			if (_instance == this) _instance = null;
		}

		private void Update()
		{
			if (_lights.Count == 0) return;
			if (Time.unscaledTime < _nextCull) return;
			_nextCull = Time.unscaledTime + CullInterval;

			var cam = Camera.main;
			if (cam == null) return;
			Vector3 eye = cam.transform.position;

			float sqr = ActiveRadius * ActiveRadius;
			for (int i = _lights.Count - 1; i >= 0; i--)
			{
				var light = _lights[i];
				if (light == null)
				{
					// Building was cleared/rebuilt — drop the stale slot (swap-remove keeps this O(1)).
					int last = _lights.Count - 1;
					_lights[i] = _lights[last];
					_positions[i] = _positions[last];
					_lights.RemoveAt(last);
					_positions.RemoveAt(last);
					continue;
				}

				Vector3 d = _positions[i] - eye;
				bool near = d.x * d.x + d.z * d.z <= sqr;
				if (light.enabled != near) light.enabled = near;
			}
		}
	}
}
