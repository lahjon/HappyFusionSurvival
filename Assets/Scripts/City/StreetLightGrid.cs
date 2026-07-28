using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Places and culls the city's street lighting.
	///
	/// <para>Street lights are the run's primary light source and the reason the street is dangerous — they are
	/// what makes an avenue readable to a monster and a player crossing it visible from a block away. So there
	/// have to be a lot of them, laid out along every road, at a density that varies by district: a downtown
	/// avenue is lit end to end, an industrial service road has one working lamp every forty metres.</para>
	///
	/// <para>Which means several thousand lights over a square kilometre, and no renderer will accept that as
	/// real-time lights. This component instantiates the posts (cheap static geometry) but keeps only the
	/// <see cref="Light"/> components near a player enabled. The result is a city that looks continuously lit
	/// while only ever costing a few dozen active lights.</para>
	/// </summary>
	public sealed class StreetLightGrid : MonoBehaviour
	{
		[Tooltip("Lights within this distance of an observer are live. Beyond it the post stays, the light dies.")]
		[Min(10f)] public float ActiveRadius = 70f;

		[Tooltip("Seconds between culling passes. Lights do not need to react faster than a player can walk.")]
		[Min(0.05f)] public float CullInterval = 0.3f;

		private readonly List<Light> _lights = new List<Light>(2048);
		private readonly List<Vector3> _positions = new List<Vector3>(2048);
		private readonly List<bool> _active = new List<bool>(2048);
		private List<Transform> _observers;
		private float _nextCull;

		/// <summary>
		/// Instantiates a light post at every sampled point along the road network. Spacing comes from the
		/// district each point falls in, so lighting density is a property of the neighbourhood rather than a
		/// global constant.
		/// </summary>
		public void Build(CityLayout layout, CitySettings settings, GameObject postPrefab, int seed)
		{
			settings ??= CitySettings.CreateDefault();

			var rng = RunRng.ForRun(seed, RngStream.Content);

			for (int r = 0; r < layout.Roads.Count; r++)
			{
				var road = layout.Roads[r];

				// Alleys are deliberately unlit. They are the covered ground route, and they only work as one if
				// they are dark.
				if (road.Class == RoadClass.Alley) continue;

				float spacing = SpacingFor(layout, settings, road);
				float length = road.Horizontal ? road.Area.width : road.Area.height;
				int count = Mathf.Max(1, Mathf.FloorToInt(length / spacing));

				for (int i = 0; i <= count; i++)
				{
					float t = count == 0 ? 0.5f : i / (float)count;

					// Posts alternate kerbs so the pools of light stagger rather than forming a corridor.
					float side = (i & 1) == 0 ? 0.42f : -0.42f;

					Vector3 position = road.Horizontal
						? new Vector3(Mathf.Lerp(road.Area.xMin, road.Area.xMax, t), 0f,
							road.Area.center.y + road.Area.height * side)
						: new Vector3(road.Area.center.x + road.Area.width * side, 0f,
							Mathf.Lerp(road.Area.yMin, road.Area.yMax, t));

					// A proportion of lamps are simply out. Dark stretches are what let a careful team cross a
					// street at all, and they make the lit stretches mean something.
					if (rng.Chance(BrokenChanceFor(layout, position))) continue;

					var light = postPrefab != null
						? Instantiate(postPrefab, position, Quaternion.identity, transform).GetComponentInChildren<Light>(true)
						: BuildProceduralPost(position);
					if (light == null) continue;

					_lights.Add(light);
					_positions.Add(position);
					_active.Add(true);
					light.enabled = false;
				}
			}
		}

		private static float SpacingFor(CityLayout layout, CitySettings settings, RoadSegment road)
		{
			var district = DistrictAt(layout, road.Center);
			var profile = settings.Profile(district != null ? district.Type : DistrictType.Commercial);

			// Arterials are the bright ones — that is what makes them the worst place to be.
			return road.Class == RoadClass.Arterial
				? profile.StreetLightSpacing * 0.75f
				: profile.StreetLightSpacing;
		}

		private static float BrokenChanceFor(CityLayout layout, Vector3 position)
		{
			var district = DistrictAt(layout, position);
			return district == null ? 0.2f : district.Type switch
			{
				DistrictType.Downtown   => 0.05f,
				DistrictType.Civic      => 0.08f,
				DistrictType.Business   => 0.1f,
				DistrictType.Commercial => 0.18f,
				DistrictType.Residential => 0.28f,
				DistrictType.Industrial => 0.45f,
				_                       => 0.2f,
			};
		}

		private static CityDistrict DistrictAt(CityLayout layout, Vector3 position)
		{
			var p = new Vector2(position.x, position.z);
			for (int i = 0; i < layout.Districts.Count; i++)
				if (layout.Districts[i].Area.Contains(p)) return layout.Districts[i];
			return null;
		}

		/// <summary>Share the streamer's observer list rather than keeping a second one in sync.</summary>
		public void SetObservers(List<Transform> observers) => _observers = observers;

		/// <summary>
		/// Sodium-vapour lamp built from primitives when no art prefab is wired: dark pole, warm emissive head
		/// (visible across the whole city even when the actual light is culled), and a downward spot that makes
		/// the pool of light on the tarmac the thing players learn to avoid.
		/// </summary>
		private Light BuildProceduralPost(Vector3 position)
		{
			var post = new GameObject("Lamp");
			post.transform.SetParent(transform, false);
			post.transform.position = position;

			var pole = new GameObject("Pole");
			pole.transform.SetParent(post.transform, false);
			var poleMb = new MeshBuilder();
			poleMb.AddBox(new Vector3(-0.07f, 0f, -0.07f), new Vector3(0.07f, 4.4f, 0.07f));
			poleMb.AddBox(new Vector3(-0.06f, 4.25f, -0.06f), new Vector3(0.06f, 4.37f, 0.75f)); // arm over the road
			AttachMesh(pole, poleMb.ToMesh("LampPole"), CityMaterials.LampPole, withCollider: true);

			var head = new GameObject("Head");
			head.transform.SetParent(post.transform, false);
			var headMb = new MeshBuilder();
			headMb.AddBox(new Vector3(-0.14f, 4.18f, 0.5f), new Vector3(0.14f, 4.3f, 0.95f));
			AttachMesh(head, headMb.ToMesh("LampHead"), CityMaterials.LampHead, withCollider: false);

			var lightGo = new GameObject("Light");
			lightGo.transform.SetParent(post.transform, false);
			lightGo.transform.localPosition = new Vector3(0f, 4.15f, 0.72f);
			lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

			var light = lightGo.AddComponent<Light>();
			light.type = LightType.Spot;
			light.range = 17f;
			light.spotAngle = 115f;
			light.innerSpotAngle = 55f;
			light.intensity = 2.6f;
			light.color = new Color(1f, 0.82f, 0.58f);
			light.shadows = LightShadows.None;
			return light;
		}

		private static void AttachMesh(GameObject go, Mesh mesh, Material material, bool withCollider)
		{
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = material;
			if (withCollider)
			{
				// Runtime-added BoxColliders do not auto-fit the mesh the way the editor Reset does.
				var box = go.AddComponent<BoxCollider>();
				box.center = mesh.bounds.center;
				box.size = mesh.bounds.size;
			}
			go.AddComponent<RuntimeMesh>().Mesh = mesh;
		}

		private void Update()
		{
			if (_lights.Count == 0) return;
			if (Time.unscaledTime < _nextCull) return;
			_nextCull = Time.unscaledTime + CullInterval;

			float sqr = ActiveRadius * ActiveRadius;

			// Static-city mode has no streamer observer list — the local camera is the only view that matters
			// for light culling, since lights are cosmetic and monsters read exposure from the layout instead.
			Transform fallback = (_observers == null || _observers.Count == 0) && Camera.main != null
				? Camera.main.transform
				: null;

			for (int i = 0; i < _lights.Count; i++)
			{
				bool near = false;

				if (fallback != null)
				{
					Vector3 delta = fallback.position - _positions[i];
					near = delta.x * delta.x + delta.z * delta.z <= sqr;
				}
				else if (_observers != null)
				{
					for (int o = 0; o < _observers.Count && !near; o++)
					{
						var observer = _observers[o];
						if (observer == null) continue;

						Vector3 delta = observer.position - _positions[i];
						near = delta.x * delta.x + delta.z * delta.z <= sqr;
					}
				}

				if (near == _active[i] && _lights[i].enabled == near) continue;

				_active[i] = near;
				if (_lights[i] != null) _lights[i].enabled = near;
			}
		}
	}
}
