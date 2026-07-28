namespace Starter.City
{
	/// <summary>
	/// Character of a district. Drives street density, lot sizes, building archetypes, street-light spacing
	/// and monster density. Assigned by <see cref="CityLayoutGenerator"/> from a district's distance to the
	/// city centre plus a deterministic roll, so a run always has a recognisable downtown core.
	/// </summary>
	public enum DistrictType : byte
	{
		/// <summary>Glass towers, plazas, wide avenues. Tallest buildings, best rooftop routes, brightest streets.</summary>
		Downtown = 0,

		/// <summary>Mid-rise offices and corporate campuses. Where objective terminals like to live.</summary>
		Business = 1,

		/// <summary>Shopfronts, markets, arcades, neon. Dense small lots, lots of loot, awnings to move under.</summary>
		Commercial = 2,

		/// <summary>Warehouses, yards, container stacks, substations. Sparse lighting — the safest streets.</summary>
		Industrial = 3,

		/// <summary>Apartment blocks and row housing. Fire escapes everywhere; the traversal playground.</summary>
		Residential = 4,

		/// <summary>Civic core — city hall, library, transit hub, hospital. Hard locks, high-value loot.</summary>
		Civic = 5,
	}

	/// <summary>
	/// What occupies a single lot. Not every lot holds a building — parks, lots and plazas are what keep the
	/// city from being a solid wall of geometry, and they double as the open, exposed spaces players are meant
	/// to dread crossing.
	/// </summary>
	public enum LotUse : byte
	{
		Building = 0,
		ParkingLot = 1,
		Plaza = 2,
		Park = 3,
		/// <summary>Fenced yard with container stacks / scaffolding. Climbable clutter, no interior.</summary>
		Yard = 4,
		/// <summary>Construction site — partial floors, exposed stairs, a vertical shortcut.</summary>
		ConstructionSite = 5,
		Empty = 6,
	}

	/// <summary>
	/// Building archetype. Selects the floorplan ruleset in <see cref="BuildingPlanGenerator"/>, the loot
	/// profile, and which <see cref="BuildingKit"/> dresses it.
	/// </summary>
	public enum BuildingArchetype : byte
	{
		/// <summary>Tall curtain-wall tower. Central core, ring corridor, open-plan floors.</summary>
		HighRiseOffice = 0,

		/// <summary>Mid-rise office. Double-loaded corridor, cellular offices, one stair core.</summary>
		OfficeBlock = 1,

		/// <summary>Ground-floor retail with a stockroom and (sometimes) flats above.</summary>
		Shop = 2,

		/// <summary>Big open retail floor, aisles, back-of-house corridor, loading bay.</summary>
		Supermarket = 3,

		/// <summary>Forecourt canopy, kiosk, service bay. Small, lit, dangerously exposed.</summary>
		GasStation = 4,

		/// <summary>Apartment block. Stair core, flats off a landing, fire escape on the rear facade.</summary>
		ApartmentBlock = 5,

		/// <summary>Warehouse — one huge volume, mezzanine office, roller shutters.</summary>
		Warehouse = 6,

		/// <summary>Multi-storey car park. Open sides, ramps between decks, a fast vertical route.</summary>
		ParkingGarage = 7,

		/// <summary>Civic hall / library / transit. Grand ground floor, restricted upper floors.</summary>
		CivicHall = 8,

		/// <summary>Small utility structure — substation, pump house, comms shed. One room, often the power gate.</summary>
		Utility = 9,
	}

	/// <summary>Road hierarchy. Width, lighting density and how exposed the road feels all key off this.</summary>
	public enum RoadClass : byte
	{
		/// <summary>Multi-lane avenue between districts. Widest, brightest, worst place to be caught.</summary>
		Arterial = 0,

		/// <summary>Ordinary street between blocks.</summary>
		Street = 1,

		/// <summary>Narrow service alley inside a block. Dark, cluttered, the preferred ground route.</summary>
		Alley = 2,
	}

	/// <summary>Role a generated room plays. Content placement, barrier anchoring and objective placement all
	/// filter on this rather than inspecting geometry.</summary>
	public enum RoomKind : byte
	{
		Generic = 0,
		/// <summary>Circulation. Never holds objectives; always connects.</summary>
		Corridor = 1,
		/// <summary>Vertical core — stairs (and lifts in tall buildings). Present on every floor at the same footprint.</summary>
		StairCore = 2,
		/// <summary>Entrance lobby on the ground floor.</summary>
		Lobby = 3,
		Office = 4,
		/// <summary>Bigger meeting/board room. Good objective host.</summary>
		Conference = 5,
		/// <summary>Server / comms room. Prime objective + navigation terminal host.</summary>
		ServerRoom = 6,
		/// <summary>Electrical room. Where power barriers get their breaker.</summary>
		Utility = 7,
		Storage = 8,
		Retail = 9,
		Apartment = 10,
		Bathroom = 11,
		/// <summary>Open roof surface. Connects to neighbouring roofs via planks and to the interior via a hatch.</summary>
		Roof = 12,
		/// <summary>Open parking deck.</summary>
		Deck = 13,
		/// <summary>Security office — keycards, camera feeds, clue notes.</summary>
		Security = 14,
	}

	/// <summary>How a player passes from one place to another. Used both for interior door graphs and for the
	/// exterior traversal graph, so route-finding code can reason about "can we get there without the street".</summary>
	public enum PortalKind : byte
	{
		/// <summary>Open threshold — no door, always passable.</summary>
		Opening = 0,
		Door = 1,
		/// <summary>Stairs between floors.</summary>
		Stair = 2,
		/// <summary>Lift. Needs building power.</summary>
		Elevator = 3,
		/// <summary>Roof hatch / bulkhead.</summary>
		RoofHatch = 4,
		/// <summary>Window — breakable or climbable entry from a ledge or fire escape.</summary>
		Window = 5,
		/// <summary>Exterior fire escape ladder/stair on the facade.</summary>
		FireEscape = 6,
		/// <summary>Roof-to-roof gap. Crossable only with a plank (or a short enough jump).</summary>
		RoofGap = 7,
		/// <summary>Climbable facade feature — drainpipe, scaffold, ledge chain.</summary>
		Climb = 8,
		/// <summary>Roller shutter / loading bay.</summary>
		Shutter = 9,
		/// <summary>Ground-level street connection between two lots.</summary>
		Street = 10,
	}
}
