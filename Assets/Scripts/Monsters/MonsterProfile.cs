using UnityEngine;

namespace Starter.Monsters
{
	/// <summary>
	/// The kinds of thing out there. Each sees the world differently, so the counterplay differs: you break
	/// line of sight on a Stalker, you stop moving for a Listener, and you stay off open ground near a Sentinel.
	/// Players never learn which is which by fighting them — only by surviving one.
	/// </summary>
	public enum MonsterType : byte
	{
		/// <summary>Fast, sight-driven, roams widely. Break line of sight and keep breaking it.</summary>
		Stalker = 0,

		/// <summary>Blind. Hunts sound — running, breaking glass, prying metal. Walk, and it walks past you.</summary>
		Listener = 1,

		/// <summary>Slow, patrols one district, enormous sight range across open ground and almost none in cover.
		/// The reason crossing a plaza is a decision.</summary>
		Sentinel = 2,
	}

	/// <summary>Tuning for one monster type.</summary>
	[System.Serializable]
	public sealed class MonsterProfile
	{
		public MonsterType Type;

		[Header("Movement")]
		[Tooltip("Metres per second while patrolling or investigating.")]
		[Min(0.1f)] public float PatrolSpeed = 1.6f;

		[Tooltip("Metres per second while hunting a player. Should out-run a walk and lose to a sprint — " +
			"escapable, but only by spending stamina and making noise.")]
		[Min(0.1f)] public float HuntSpeed = 4.2f;

		[Header("Senses")]
		[Tooltip("Maximum sight distance in the open. 0 for a blind type.")]
		[Min(0f)] public float SightRange = 35f;

		[Tooltip("Vision cone, degrees.")]
		[Range(10f, 360f)] public float SightAngle = 110f;

		[Tooltip("Distance at which ordinary movement noise is heard.")]
		[Min(0f)] public float HearingRange = 14f;

		[Tooltip("Multiplier applied to sight range when the target is on open ground — a road, a plaza, a car " +
			"park. This is the number that makes streets dangerous.")]
		[Min(1f)] public float OpenGroundSightMultiplier = 2.2f;

		[Tooltip("Multiplier applied to sight range when the target is inside a building or an alley.")]
		[Range(0.05f, 1f)] public float CoveredSightMultiplier = 0.45f;

		[Header("Behaviour")]
		[Tooltip("Seconds it keeps hunting after losing the target.")]
		[Min(0.5f)] public float LoseInterestSeconds = 12f;

		[Tooltip("Distance at which it kills. Fighting is not an option — this is the whole interaction.")]
		[Min(0.5f)] public float KillRange = 1.8f;

		[Tooltip("How far it wanders from its home node while patrolling.")]
		[Min(10f)] public float PatrolRadius = 120f;
	}

	/// <summary>What a monster is currently doing. Networked so remote peers can drive the right animation and
	/// audio without re-running the AI.</summary>
	public enum MonsterState : byte
	{
		/// <summary>Wandering its patrol area.</summary>
		Patrol = 0,
		/// <summary>Heading to a noise or a last-known position.</summary>
		Investigate = 1,
		/// <summary>Has a target and is closing.</summary>
		Hunt = 2,
		/// <summary>Sweeping an area after an objective went live.</summary>
		Search = 3,
	}
}
