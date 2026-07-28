using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.City
{
	/// <summary>
	/// Fallback materials for city geometry built without a <see cref="BuildingKit"/>. A renderer with no
	/// material draws magenta, so every mesh the builder emits must get <em>something</em> — these are flat
	/// URP Lit tints, created once and shared, dark enough to sit in a night city without looking like an
	/// error. A kit material always wins when one is wired.
	///
	/// <para>Buildings pull from a small muted facade palette keyed by lot index, so the skyline reads as many
	/// different buildings instead of one grey mass — while the whole city still only ever uses a dozen
	/// materials, which keeps batching intact.</para>
	/// </summary>
	public static class CityMaterials
	{
		private static Material _floor, _ground, _road, _pavement;
		private static Material _propWood, _propMetal, _propSoft;
		private static Material _lampPole, _lampHead;
		private static Material[] _walls;

		// Muted night-city facade tints: concrete, brick, sandstone, painted stucco, weathered blues/greens.
		private static readonly Color[] WallColors =
		{
			new Color(0.42f, 0.41f, 0.40f), // concrete grey
			new Color(0.45f, 0.33f, 0.28f), // brick red
			new Color(0.48f, 0.44f, 0.35f), // sandstone
			new Color(0.35f, 0.38f, 0.42f), // blue-grey
			new Color(0.38f, 0.42f, 0.36f), // faded green
			new Color(0.50f, 0.46f, 0.42f), // warm plaster
			new Color(0.30f, 0.30f, 0.33f), // dark slate
			new Color(0.46f, 0.40f, 0.46f), // mauve stucco
			new Color(0.33f, 0.28f, 0.24f), // dark brown
			new Color(0.52f, 0.50f, 0.44f), // pale limestone
		};

		public static Material Floor    => Get(ref _floor,    "City_Floor",    new Color(0.30f, 0.29f, 0.28f));
		public static Material Ground   => Get(ref _ground,   "City_Ground",   new Color(0.16f, 0.16f, 0.15f));
		public static Material Road     => Get(ref _road,     "City_Road",     new Color(0.11f, 0.11f, 0.12f));
		public static Material Pavement => Get(ref _pavement, "City_Pavement", new Color(0.24f, 0.24f, 0.23f));

		public static Material PropWood  => Get(ref _propWood,  "City_PropWood",  new Color(0.40f, 0.29f, 0.19f));
		public static Material PropMetal => Get(ref _propMetal, "City_PropMetal", new Color(0.22f, 0.24f, 0.26f));
		public static Material PropSoft  => Get(ref _propSoft,  "City_PropSoft",  new Color(0.34f, 0.30f, 0.35f));

		public static Material LampPole => Get(ref _lampPole, "City_LampPole", new Color(0.15f, 0.16f, 0.17f));

		/// <summary>Emissive lamp head — visible as a lit point from across the city even when its actual Light
		/// component has been culled by the street-light grid.</summary>
		public static Material LampHead
		{
			get
			{
				if (_lampHead != null) return _lampHead;
				_lampHead = Get(ref _lampHead, "City_LampHead", new Color(1f, 0.87f, 0.66f));
				_lampHead.EnableKeyword("_EMISSION");
				_lampHead.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
				_lampHead.SetColor("_EmissionColor", new Color(1f, 0.82f, 0.55f) * 2.2f);
				return _lampHead;
			}
		}

		private static Material _interiorFixture;

		/// <summary>Emissive interior light fixture — the cool fluorescent counterpart to <see cref="LampHead"/>.
		/// Lit rooms stay visibly lit from outside even when their Light has been distance-culled.</summary>
		public static Material InteriorFixture
		{
			get
			{
				if (_interiorFixture != null) return _interiorFixture;
				_interiorFixture = Get(ref _interiorFixture, "City_InteriorFixture", new Color(0.92f, 0.95f, 0.92f));
				_interiorFixture.EnableKeyword("_EMISSION");
				_interiorFixture.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
				_interiorFixture.SetColor("_EmissionColor", new Color(0.85f, 0.95f, 0.88f) * 1.8f);
				return _interiorFixture;
			}
		}

		/// <summary>Default facade tint (first palette entry) for callers with no building identity.</summary>
		public static Material Wall => WallVariant(0);

		/// <summary>Stable facade tint for a building — same lot index, same color, on every peer.</summary>
		public static Material WallVariant(int lotIndex)
		{
			_walls ??= new Material[WallColors.Length];
			int i = Mathf.Abs(lotIndex) % WallColors.Length;
			if (_walls[i] == null)
			{
				Material slot = null;
				_walls[i] = Get(ref slot, $"City_Wall_{i}", WallColors[i]);
			}
			return _walls[i];
		}

		/// <summary>Kit material when present, shared fallback otherwise. Never returns null.</summary>
		public static Material Or(Material kitMaterial, Material fallback) =>
			kitMaterial != null ? kitMaterial : fallback;

		private static Material Get(ref Material slot, string name, Color color)
		{
			if (slot != null) return slot;

			// URP Lit is always part of a URP project; the pipeline's default material is the safety net for
			// any other pipeline. Neither path touches Resources.
			var shader = Shader.Find("Universal Render Pipeline/Lit");
			slot = shader != null
				? new Material(shader)
				: new Material(GraphicsSettings.currentRenderPipeline != null
					? GraphicsSettings.currentRenderPipeline.defaultMaterial
					: new Material(Shader.Find("Standard")));

			slot.name = name;
			slot.color = color;
			slot.SetFloat("_Smoothness", 0.1f);
			return slot;
		}
	}
}
