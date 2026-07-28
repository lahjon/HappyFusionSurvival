using System;

namespace Starter.Run
{
	/// <summary>
	/// Logical currencies of the randomizer. These are <b>not</b> inventory items — they are what the solver
	/// reasons about. Most map onto a real <c>ItemDefinition</c> (a crowbar is a crowbar), but some are pure
	/// knowledge (<see cref="PinCode"/>, <see cref="Intel"/>) and one is a world state (<see cref="Power"/>).
	/// Keeping them abstract is what lets the same solver handle "you need the bolt cutters" and "you need to
	/// know the code" and "you need the substation switched on" without special cases.
	/// </summary>
	public enum RunItemKind : byte
	{
		None = 0,

		/// <summary>Electronic keycard. <c>Variant</c> is the card's tier/colour — cards are not interchangeable.</summary>
		Keycard = 1,

		/// <summary>Physical key or key ring. <c>Variant</c> identifies which lock it fits.</summary>
		Key = 2,

		/// <summary>Knowledge of a numeric door code. <c>Variant</c> identifies the keypad. Can be found written
		/// down, or bought.</summary>
		PinCode = 3,

		/// <summary>Lockpick set. Generic — opens any pickable lock, but slowly and noisily.</summary>
		Lockpick = 4,

		/// <summary>Crowbar. Pries service doors and shutters.</summary>
		Crowbar = 5,

		/// <summary>Bolt cutters. Chains, padlocks, fences.</summary>
		BoltCutters = 6,

		/// <summary>Rope / grapple. The vertical traversal unlock — facades, fire escapes, drops.</summary>
		Rope = 7,

		/// <summary>Planks. Bridge a roof-to-roof gap.</summary>
		Planks = 8,

		/// <summary>Building power restored. <c>Variant</c> is the lot index of the building (or a district id for
		/// a substation). A world state, granted by throwing a breaker, not something carried.</summary>
		Power = 9,

		/// <summary>Knowledge of where an objective is. <c>Variant</c> is the objective index. This is the clue
		/// layer — the thing players buy, find written on a wall, or read off a navigation terminal.</summary>
		Intel = 10,

		/// <summary>A navigation terminal has been used. <c>Variant</c> is the terminal index. Chaining these is
		/// how the map opens up.</summary>
		NavUnlock = 11,
	}

	/// <summary>One logical requirement or reward: a kind plus a variant that distinguishes instances.</summary>
	[Serializable]
	public struct RunItem : IEquatable<RunItem>
	{
		public RunItemKind Kind;
		public int Variant;

		public RunItem(RunItemKind kind, int variant = 0)
		{
			Kind = kind;
			Variant = variant;
		}

		public static readonly RunItem None = new RunItem(RunItemKind.None);

		public bool IsNone => Kind == RunItemKind.None;

		/// <summary>True when this is knowledge rather than a carried object. Knowledge is shared by the whole
		/// team the moment one player learns it — four players should not have to each read the same note.</summary>
		public bool IsKnowledge => Kind is RunItemKind.PinCode or RunItemKind.Intel or RunItemKind.NavUnlock or RunItemKind.Power;

		public bool Equals(RunItem other) => Kind == other.Kind && Variant == other.Variant;
		public override bool Equals(object obj) => obj is RunItem other && Equals(other);
		public override int GetHashCode() => ((int)Kind << 20) ^ Variant;
		public override string ToString() => Variant != 0 ? $"{Kind}#{Variant}" : Kind.ToString();

		/// <summary>Short player-facing description, used in clue text and locked-door prompts.</summary>
		public string Describe() => Kind switch
		{
			RunItemKind.Keycard     => $"a tier-{Variant} keycard",
			RunItemKind.Key         => "a door key",
			RunItemKind.PinCode     => "the door code",
			RunItemKind.Lockpick    => "a lockpick set",
			RunItemKind.Crowbar     => "a crowbar",
			RunItemKind.BoltCutters => "bolt cutters",
			RunItemKind.Rope        => "a climbing rope",
			RunItemKind.Planks      => "planks",
			RunItemKind.Power       => "building power",
			RunItemKind.Intel       => "intel on the location",
			RunItemKind.NavUnlock   => "network access",
			_                       => "nothing",
		};
	}

	/// <summary>
	/// The physical form a block takes. Kind is presentation and interaction; what actually opens it is the
	/// <see cref="RunItem"/> requirement attached alongside. That split is what makes the system modular — the
	/// planner can put a keypad on a stairwell this run and a welded shutter on the same stairwell next run
	/// without either the building generator or the door code knowing anything changed.
	/// </summary>
	public enum BarrierKind : byte
	{
		/// <summary>Deadbolt / mechanical lock. Opens with the matching key, or can be picked.</summary>
		KeyedLock = 0,

		/// <summary>Numeric keypad. Opens with the code — which is knowledge, so it can be shouted to a teammate.</summary>
		Keypad = 1,

		/// <summary>Electronic card reader. Needs the matching card and building power.</summary>
		CardReader = 2,

		/// <summary>Roller shutter / security grille. Pried with a crowbar or cut open.</summary>
		Shutter = 3,

		/// <summary>Chained doors. Bolt cutters only.</summary>
		Chained = 4,

		/// <summary>Powered door — a mag-lock or motorised gate that simply will not move without building power.</summary>
		PowerGate = 5,

		/// <summary>Welded or collapsed. Never opens. Its whole job is to force players onto another route.</summary>
		Sealed = 6,

		/// <summary>Not a door at all: a facade the players must climb. Needs rope; costs time and exposure.</summary>
		ClimbOnly = 7,
	}

	public static class BarrierKindExtensions
	{
		/// <summary>True when a lockpick set can defeat this kind, regardless of its own requirement. Pickable
		/// barriers are the randomizer's safety valve: they keep a bad key shuffle from being unwinnable.</summary>
		public static bool IsPickable(this BarrierKind kind) => kind is BarrierKind.KeyedLock;

		/// <summary>True when this kind can never be opened and exists only to close off a route.</summary>
		public static bool IsHardBlock(this BarrierKind kind) => kind == BarrierKind.Sealed;

		public static string Label(this BarrierKind kind) => kind switch
		{
			BarrierKind.KeyedLock  => "Locked",
			BarrierKind.Keypad     => "Keypad",
			BarrierKind.CardReader => "Card reader",
			BarrierKind.Shutter    => "Shutter down",
			BarrierKind.Chained    => "Chained",
			BarrierKind.PowerGate  => "No power",
			BarrierKind.Sealed     => "Sealed",
			BarrierKind.ClimbOnly  => "No way up from here",
		};
	}

	/// <summary>How players get into a gated building. Each site offers several; any one of them is enough,
	/// which is what turns a locked building from a wall into a puzzle.</summary>
	public enum EntryRouteKind : byte
	{
		/// <summary>Through the front door.</summary>
		FrontDoor = 0,
		/// <summary>Loading bay, fire exit, back door.</summary>
		Service = 1,
		/// <summary>Up the facade — fire escape, drainpipe, ledges — and in through a window.</summary>
		Facade = 2,
		/// <summary>Across the roofs from a neighbouring building, down through the hatch.</summary>
		Rooftop = 3,
		/// <summary>Through a basement / parking connection from the block.</summary>
		Underground = 4,
	}
}
