using System.Collections.Generic;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>What a gated location is for.</summary>
	public enum SiteKind : byte
	{
		/// <summary>One of the four terminals that must be activated to win.</summary>
		Objective = 0,
		/// <summary>A navigation terminal — activating it reveals part of the city and hands out intel.</summary>
		NavTerminal = 1,
		/// <summary>A secured stash worth breaking into for the gear inside, but not required to win.</summary>
		Vault = 2,
	}

	/// <summary>What sits in a placed slot.</summary>
	public enum StashKind : byte
	{
		/// <summary>A lootable container in a room.</summary>
		Container = 0,
		/// <summary>A readable note / whiteboard / sticky on a monitor. How codes and intel are found.</summary>
		Note = 1,
		/// <summary>A vendor's stock. Costs money instead of exploration — the "purchase a clue" path.</summary>
		VendorStock = 2,
		/// <summary>An electrical breaker. Throwing it grants <see cref="RunItemKind.Power"/> for a district.</summary>
		Breaker = 3,
	}

	/// <summary>
	/// One block placed on one portal. Everything about how it behaves at runtime is here: what it looks like
	/// (<see cref="Kind"/>), what defeats it (<see cref="Requirement"/>), and where it physically is.
	/// </summary>
	public sealed class BarrierSpec
	{
		public int Index;
		public BarrierKind Kind;

		/// <summary>Lot whose building this barrier belongs to.</summary>
		public int LotIndex;

		/// <summary>Index into that building's <c>Portals</c>. -1 for a free-standing barricade.</summary>
		public int PortalIndex = -1;

		/// <summary>What opens it. <see cref="RunItem.None"/> means it opens on interaction (it is only there to
		/// cost time and noise).</summary>
		public RunItem Requirement;

		/// <summary>Owning site, or -1 when it gates something optional.</summary>
		public int SiteIndex = -1;

		/// <summary>Which entry route it belongs to, or -1 when it is an interior barrier past the entry.</summary>
		public int RouteIndex = -1;

		/// <summary>Numeric code for <see cref="BarrierKind.Keypad"/>. Regenerated per run.</summary>
		public string Code;

		/// <summary>Player-facing name — "Lobby doors", "Stairwell B", "Server room".</summary>
		public string Label;

		public Vector3 Position;
		public Vector3 Facing;
	}

	/// <summary>One way into a site. Every barrier in <see cref="BarrierIndices"/> must be passed, but any single
	/// route being open is enough to get in.</summary>
	public sealed class EntryRoute
	{
		public EntryRouteKind Kind;
		public readonly List<int> BarrierIndices = new List<int>(2);

		/// <summary>Portal the route finally comes through, for signposting and AI pathing.</summary>
		public int PortalIndex = -1;
	}

	/// <summary>
	/// A gated location: a building, a specific room inside it, the ways in, and the blocks in the way. The
	/// planner produces these; the solver only ever looks at their barriers.
	/// </summary>
	public sealed class RunSite
	{
		public int Index;
		public SiteKind Kind;

		public int LotIndex;
		public int TargetLevel;
		public int TargetRoomIndex;
		public Vector3 TargetPosition;

		/// <summary>Alternate ways in. At least one is always solvable — the planner guarantees it.</summary>
		public readonly List<EntryRoute> Routes = new List<EntryRoute>(3);

		/// <summary>Barriers between the entry and the target room, in series.</summary>
		public readonly List<int> InteriorBarriers = new List<int>(2);

		/// <summary>Cached address string for clue text ("Kessler Tower, the 14th floor").</summary>
		public string Address;
	}

	/// <summary>A placed slot and what it holds.</summary>
	public sealed class StashSpec
	{
		public int Index;
		public StashKind Kind;

		public int LotIndex;
		public int Level;
		public int RoomIndex;
		public Vector3 Position;

		/// <summary>Logic region this slot sits in. The solver only lets players take from reachable regions.</summary>
		public int RegionId;

		/// <summary>What it holds. <see cref="RunItem.None"/> means ordinary, non-logical loot.</summary>
		public RunItem Contents;

		/// <summary>For notes and vendor stock: the text shown to the player.</summary>
		public string Text;
	}

	/// <summary>
	/// The complete logical solution for one run: where the four terminals are, what is in the way, where every
	/// key lives, and where players start. Like <see cref="Starter.City.CityLayout"/> this is plain data derived
	/// from the seed, so every peer computes an identical copy and none of it is replicated.
	/// </summary>
	public sealed class RunPlanData
	{
		public int Seed;

		public readonly List<RunSite> Sites = new List<RunSite>(12);
		public readonly List<BarrierSpec> Barriers = new List<BarrierSpec>(48);
		public readonly List<StashSpec> Stashes = new List<StashSpec>(128);

		/// <summary>Indices into <see cref="Sites"/> for the four win-condition terminals, in activation-agnostic
		/// order (they can be done in any sequence).</summary>
		public readonly List<int> ObjectiveSites = new List<int>(4);

		/// <summary>Indices into <see cref="Sites"/> for navigation terminals.</summary>
		public readonly List<int> NavSites = new List<int>(6);

		/// <summary>Where the team starts this run. One entry — players spawn together, scattered a few metres
		/// apart — chosen from several candidate districts so the run's opening reads differently every time.</summary>
		public Vector3 StartPosition;
		public int StartLotIndex = -1;
		public string StartAddress;

		/// <summary>True once the solver has confirmed the plan is completable. A plan that fails this is thrown
		/// away and re-rolled rather than shipped — an unwinnable run is worse than a boring one.</summary>
		public bool Verified;

		/// <summary>Region count used by the solver. Region 0 is always the street network.</summary>
		public int RegionCount;

		public const int StreetRegion = 0;

		public RunSite Site(int index) => index >= 0 && index < Sites.Count ? Sites[index] : null;
		public BarrierSpec Barrier(int index) => index >= 0 && index < Barriers.Count ? Barriers[index] : null;

		/// <summary>Region id for the inside of a site, past its entry routes.</summary>
		public int InteriorRegion(int siteIndex) => 1 + siteIndex;

		/// <summary>Region id for a site's target room, past its interior barriers.</summary>
		public int TargetRegion(int siteIndex) => 1 + Sites.Count + siteIndex;
	}
}
