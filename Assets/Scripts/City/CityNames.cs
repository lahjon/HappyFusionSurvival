namespace Starter.City
{
	/// <summary>
	/// Deterministic name generation for districts and buildings.
	///
	/// <para>Names are not decoration here — they are the game's addressing system. A clue is only useful if it
	/// names a place the player can then find on a map and recognise on a sign, so every generated name has to be
	/// stable for a seed, unique enough to disambiguate, and short enough to fit on a facade.</para>
	/// </summary>
	public static class CityNames
	{
		private static readonly string[] Roots =
		{
			"Harrow", "Vance", "Kessler", "Marlow", "Ridley", "Ashgate", "Thorn", "Blackwell",
			"Corvin", "Halloway", "Dunmore", "Fenwick", "Grimsby", "Larkin", "Mercer", "Nordhaven",
			"Oakvale", "Pemberton", "Quill", "Rookwood", "Sable", "Tarrow", "Ulster", "Varnley",
			"Westbrook", "Yarrow", "Alderman", "Brackish", "Cinderfell", "Drayton", "Everly", "Foxhall",
		};

		private static readonly string[] DistrictSuffix =
		{
			"District", "Quarter", "Ward", "Heights", "Row", "End", "Crossing", "Reach",
		};

		private static readonly string[] TowerSuffix =
		{
			"Tower", "Plaza", "Center", "Heights", "Spire", "House", "Exchange",
		};

		private static readonly string[] CorporateSuffix =
		{
			"Holdings", "Industries", "Group", "Systems", "Partners", "Trust", "Analytics", "Logistics",
		};

		private static readonly string[] ShopKinds =
		{
			"Grocer", "Pharmacy", "Hardware", "Electronics", "Laundry", "Diner", "Bakery", "Pawn",
			"Liquor", "Bookshop", "Barber", "Deli",
		};

		private static readonly string[] CivicKinds =
		{
			"City Hall", "Public Library", "Central Archive", "Transit Authority", "Courthouse",
			"Municipal Records", "Water Board",
		};

		public static string District(ref RunRng rng, DistrictType type)
		{
			string root = rng.Pick(Roots);
			return type switch
			{
				DistrictType.Downtown   => $"{root} Downtown",
				DistrictType.Civic      => $"{root} Civic Center",
				DistrictType.Industrial => $"{root} Works",
				_                       => $"{root} {rng.Pick(DistrictSuffix)}",
			};
		}

		public static string Building(ref RunRng rng, BuildingArchetype archetype)
		{
			string root = rng.Pick(Roots);
			return archetype switch
			{
				BuildingArchetype.HighRiseOffice => $"{root} {rng.Pick(TowerSuffix)}",
				BuildingArchetype.OfficeBlock    => $"{root} {rng.Pick(CorporateSuffix)}",
				BuildingArchetype.Shop           => $"{root} {rng.Pick(ShopKinds)}",
				BuildingArchetype.Supermarket    => $"{root} Market",
				BuildingArchetype.GasStation     => $"{root} Fuel",
				BuildingArchetype.ApartmentBlock => $"{root} Apartments",
				BuildingArchetype.Warehouse      => $"{root} Depot",
				BuildingArchetype.ParkingGarage  => $"{root} Parking",
				BuildingArchetype.CivicHall      => rng.Pick(CivicKinds),
				BuildingArchetype.Utility        => $"{root} Substation",
				_                                => root,
			};
		}
	}
}
