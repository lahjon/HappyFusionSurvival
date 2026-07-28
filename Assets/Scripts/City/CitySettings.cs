using UnityEngine;

namespace Starter.City
{
	/// <summary>Per-district-type tuning. One entry per <see cref="DistrictType"/>, indexed by the enum value.</summary>
	[System.Serializable]
	public sealed class DistrictProfile
	{
		public DistrictType Type;

		[Header("Street grid")]
		[Tooltip("Target size of a city block along its longer axis, in metres. Smaller = denser street grid.")]
		[Min(30f)] public float TargetBlockSize = 80f;

		[Tooltip("Width of ordinary streets inside this district.")]
		[Min(4f)] public float StreetWidth = 10f;

		[Header("Lots")]
		[Tooltip("Depth of the buildable band around a block's edge. The leftover core becomes a courtyard.")]
		[Min(8f)] public float LotDepth = 22f;

		[Tooltip("Frontage width of a lot along the street, in metres (min/max).")]
		public Vector2 LotFrontage = new Vector2(16f, 28f);

		[Tooltip("Gap left between the lot boundary and the building footprint, per side.")]
		[Min(0f)] public float Setback = 1.5f;

		[Header("Buildings")]
		[Tooltip("Storey count range for buildings here (min inclusive, max inclusive).")]
		public Vector2Int Floors = new Vector2Int(2, 5);

		[Tooltip("Chance a lot in this district is left open (park / plaza / parking / yard) instead of built on. " +
			"Open ground is exposed ground — this is the main dial for how dangerous the district feels to cross.")]
		[Range(0f, 1f)] public float OpenLotChance = 0.15f;

		[Tooltip("Chance a building here has usable roof access (hatch or bulkhead). Drives how viable the rooftop " +
			"route network is in this district.")]
		[Range(0f, 1f)] public float RoofAccessChance = 0.5f;

		[Tooltip("Chance a building here carries an exterior fire escape.")]
		[Range(0f, 1f)] public float FireEscapeChance = 0.35f;

		[Header("Atmosphere")]
		[Tooltip("Spacing between street lights along roads here. Larger = darker district.")]
		[Min(8f)] public float StreetLightSpacing = 28f;

		[Tooltip("Relative monster density. 1 = baseline.")]
		[Min(0f)] public float MonsterDensity = 1f;

		[Tooltip("Relative weight for holding an objective terminal. Business/Civic should dominate.")]
		[Min(0f)] public float ObjectiveWeight = 1f;

		[Tooltip("Archetype weights for building lots here, indexed by BuildingArchetype. Length must match the enum.")]
		public float[] ArchetypeWeights;
	}

	/// <summary>
	/// Tuning for the whole city generator. Optional: <see cref="CityDirector"/> falls back to
	/// <see cref="CreateDefault"/> when no asset is assigned, so generation works before anything is authored.
	/// Create an asset to hand-tune a run feel (denser downtown, darker industrial, taller towers).
	/// </summary>
	[CreateAssetMenu(menuName = "HorrorTown/City Settings", fileName = "CitySettings")]
	public sealed class CitySettings : ScriptableObject
	{
		[Header("Extents")]
		[Tooltip("Side length of the city in metres. Current target is a compact 250 m town — dense enough that " +
			"every run crosses most of it, small enough to hold in your head after a few runs.")]
		[Min(100f)] public float CitySize = 250f;

		[Tooltip("World-space south-west corner of the city footprint.")]
		public Vector2 Origin = Vector2.zero;

		[Header("Arterial roads (district split)")]
		[Tooltip("A region larger than this on its long axis gets split by an arterial road. Scaled for the 250 m " +
			"town so it still yields a handful of distinct districts.")]
		[Min(60f)] public float MaxDistrictSize = 110f;

		[Tooltip("A region smaller than this is never split further, whatever MaxDistrictSize says.")]
		[Min(40f)] public float MinDistrictSize = 60f;

		[Tooltip("Width of the multi-lane avenues between districts. Wide, bright and lethal.")]
		[Min(8f)] public float ArterialWidth = 12f;

		[Header("Alleys")]
		[Tooltip("Blocks larger than this on their long axis get a service alley cut through the middle.")]
		[Min(40f)] public float AlleyBlockThreshold = 110f;

		[Tooltip("Width of a service alley. Narrow enough to feel covered from above.")]
		[Min(2f)] public float AlleyWidth = 4.5f;

		[Header("Split shape")]
		[Tooltip("Where along an axis a BSP split may land, as a fraction. Keeping this away from 0/1 stops slivers.")]
		public Vector2 SplitRange = new Vector2(0.4f, 0.6f);

		[Header("Districts")]
		[Tooltip("Per-district tuning, indexed by DistrictType.")]
		public DistrictProfile[] Districts;

		/// <summary>
		/// Profile for a district type.
		///
		/// <para>An unpopulated <see cref="Districts"/> array falls back to the built-in defaults rather than to a
		/// blank profile. That matters: a freshly created asset starts with an empty array, and a blank profile
		/// has no archetype weights at all — so every lot in the city would degrade to the smallest fallback
		/// archetype. Leaving the field null gives a good city; an empty asset would quietly give a worse one.
		/// This makes both paths behave the same.</para>
		/// </summary>
		public DistrictProfile Profile(DistrictType type)
		{
			var table = Districts != null && Districts.Length > 0 ? Districts : DefaultDistricts;
			int i = (int)type;
			if (i >= 0 && i < table.Length && table[i] != null) return table[i];
			return table[0];
		}

		public Rect Area => new Rect(Origin.x, Origin.y, CitySize, CitySize);

		/// <summary>Populate a newly created asset with the tuned defaults. Unity calls this when the asset is
		/// created through the CreateAssetMenu entry.</summary>
		private void Reset()
		{
			Districts = BuildDefaultDistricts();
		}

		private static DistrictProfile[] _defaultDistricts;

		/// <summary>Shared, lazily built default profile table. Built once — <see cref="CityLayoutGenerator"/>
		/// queries it per lot.</summary>
		private static DistrictProfile[] DefaultDistricts => _defaultDistricts ??= BuildDefaultDistricts();

		/// <summary>
		/// Hand-tuned defaults describing a plausible modern metropolis: a tight tower core, a business ring,
		/// dense commercial strips, sprawling residential, and dark industrial edges. Used when no asset is
		/// assigned so the generator is never blocked on authoring.
		/// </summary>
		public static CitySettings CreateDefault()
		{
			var s = CreateInstance<CitySettings>();
			s.name = "CitySettings (default)";
			s.Districts = BuildDefaultDistricts();
			return s;
		}

		/// <summary>The tuned default district table. Shared by <see cref="CreateDefault"/>, <see cref="Reset"/>
		/// and the <see cref="Profile"/> fallback so all three describe the same city.</summary>
		private static DistrictProfile[] BuildDefaultDistricts()
		{
			return new[]
			{
				new DistrictProfile
				{
					Type = DistrictType.Downtown,
					TargetBlockSize = 70f, StreetWidth = 12f, LotDepth = 28f,
					LotFrontage = new Vector2(34f, 52f), Setback = 3f,
					Floors = new Vector2Int(8, 16), OpenLotChance = 0.20f,
					RoofAccessChance = 0.75f, FireEscapeChance = 0.15f,
					StreetLightSpacing = 18f, MonsterDensity = 1.15f, ObjectiveWeight = 1.6f,
					ArchetypeWeights = Weights(
						(BuildingArchetype.HighRiseOffice, 6f), (BuildingArchetype.OfficeBlock, 3f),
						(BuildingArchetype.ParkingGarage, 1.5f), (BuildingArchetype.Shop, 1f),
						(BuildingArchetype.CivicHall, 0.5f)),
				},
				new DistrictProfile
				{
					Type = DistrictType.Business,
					TargetBlockSize = 65f, StreetWidth = 10f, LotDepth = 26f,
					LotFrontage = new Vector2(24f, 40f), Setback = 2.5f,
					Floors = new Vector2Int(4, 11), OpenLotChance = 0.15f,
					RoofAccessChance = 0.7f, FireEscapeChance = 0.35f,
					StreetLightSpacing = 22f, MonsterDensity = 1f, ObjectiveWeight = 2.2f,
					ArchetypeWeights = Weights(
						(BuildingArchetype.OfficeBlock, 6f), (BuildingArchetype.HighRiseOffice, 2f),
						(BuildingArchetype.ParkingGarage, 1.5f), (BuildingArchetype.Shop, 1.5f),
						(BuildingArchetype.ApartmentBlock, 1f)),
				},
				new DistrictProfile
				{
					Type = DistrictType.Commercial,
					TargetBlockSize = 55f, StreetWidth = 9f, LotDepth = 20f,
					LotFrontage = new Vector2(12f, 22f), Setback = 0.5f,
					Floors = new Vector2Int(1, 4), OpenLotChance = 0.12f,
					RoofAccessChance = 0.55f, FireEscapeChance = 0.5f,
					StreetLightSpacing = 20f, MonsterDensity = 1.1f, ObjectiveWeight = 0.7f,
					ArchetypeWeights = Weights(
						(BuildingArchetype.Shop, 7f), (BuildingArchetype.Supermarket, 1.5f),
						(BuildingArchetype.ApartmentBlock, 2f), (BuildingArchetype.OfficeBlock, 1.5f),
						(BuildingArchetype.GasStation, 0.8f)),
				},
				new DistrictProfile
				{
					Type = DistrictType.Industrial,
					TargetBlockSize = 80f, StreetWidth = 10f, LotDepth = 34f,
					LotFrontage = new Vector2(35f, 60f), Setback = 4f,
					Floors = new Vector2Int(1, 3), OpenLotChance = 0.35f,
					RoofAccessChance = 0.6f, FireEscapeChance = 0.25f,
					StreetLightSpacing = 45f, MonsterDensity = 0.7f, ObjectiveWeight = 0.6f,
					ArchetypeWeights = Weights(
						(BuildingArchetype.Warehouse, 7f), (BuildingArchetype.Utility, 2f),
						(BuildingArchetype.OfficeBlock, 1f), (BuildingArchetype.GasStation, 1f)),
				},
				new DistrictProfile
				{
					Type = DistrictType.Residential,
					TargetBlockSize = 60f, StreetWidth = 8f, LotDepth = 22f,
					LotFrontage = new Vector2(14f, 26f), Setback = 2f,
					Floors = new Vector2Int(2, 6), OpenLotChance = 0.22f,
					RoofAccessChance = 0.5f, FireEscapeChance = 0.75f,
					StreetLightSpacing = 30f, MonsterDensity = 0.85f, ObjectiveWeight = 0.5f,
					ArchetypeWeights = Weights(
						(BuildingArchetype.ApartmentBlock, 7f), (BuildingArchetype.Shop, 2f),
						(BuildingArchetype.ParkingGarage, 0.8f), (BuildingArchetype.Utility, 0.5f)),
				},
				new DistrictProfile
				{
					Type = DistrictType.Civic,
					TargetBlockSize = 75f, StreetWidth = 11f, LotDepth = 30f,
					LotFrontage = new Vector2(30f, 50f), Setback = 5f,
					Floors = new Vector2Int(2, 7), OpenLotChance = 0.35f,
					RoofAccessChance = 0.6f, FireEscapeChance = 0.2f,
					StreetLightSpacing = 20f, MonsterDensity = 0.9f, ObjectiveWeight = 1.8f,
					ArchetypeWeights = Weights(
						(BuildingArchetype.CivicHall, 5f), (BuildingArchetype.OfficeBlock, 2f),
						(BuildingArchetype.ParkingGarage, 1f), (BuildingArchetype.Utility, 1f)),
				},
			};
		}

		/// <summary>Build a full-length archetype weight array from a sparse list of (archetype, weight) pairs.</summary>
		private static float[] Weights(params (BuildingArchetype archetype, float weight)[] entries)
		{
			var arr = new float[System.Enum.GetValues(typeof(BuildingArchetype)).Length];
			for (int i = 0; i < entries.Length; i++)
				arr[(int)entries[i].archetype] = entries[i].weight;
			return arr;
		}
	}
}
