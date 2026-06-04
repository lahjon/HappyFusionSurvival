using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>Passive "home" routine an NPC runs while no target is engaged.</summary>
	public enum NpcBehavior : byte { Idle, Wander, Patrol }

	/// <summary>How a patrolling NPC walks its waypoints.</summary>
	public enum PatrolMode : byte { Loop, PingPong, Once }

	/// <summary>
	/// Designer-authored "NPC type". Holds all behavior tuning for an NPC and the
	/// prefab to spawn for it. Mirrors the project's other data-driven configs
	/// (<see cref="ItemDefinition"/>, RecipeDefinition). Read by <see cref="NpcAgent"/>
	/// (behavior) and <see cref="NpcSpawner"/> (which prefab, which phases).
	/// </summary>
	[CreateAssetMenu(fileName = "Npc", menuName = "AI/NPC Definition", order = 0)]
	public sealed class NpcDefinition : ScriptableObject
	{
		[Header("Identity")]
		public string DisplayName = "NPC";
		[Tooltip("Networked prefab spawned for this type. Must have an NpcAgent + NetworkObject + NavMeshAgent.")]
		public NpcAgent Prefab;

		[Header("Passive Behavior")]
		[Tooltip("What the NPC does when no player is being chased: stand, roam its area, or walk waypoints.")]
		public NpcBehavior Behavior = NpcBehavior.Wander;
		[Tooltip("NavMeshAgent speed during the passive routine (wander / patrol / return-home).")]
		[Min(0f)] public float MoveSpeed = 1.5f;
		[Tooltip("How close to a destination counts as arrived (meters).")]
		[Min(0.01f)] public float ArrivalTolerance = 0.3f;

		[Header("Idle")]
		[Tooltip("If true, an Idle NPC slowly rotates in place instead of standing perfectly still.")]
		public bool LookAround = false;
		[Tooltip("Degrees per second the NPC rotates while looking around.")]
		[Min(0f)] public float LookAroundSpeed = 30f;

		[Header("Wander")]
		[Tooltip("Radius around the spawn point the NPC roams within when NO NpcMovementArea is assigned. Ignored if an Area is set (the box bounds it instead).")]
		[Min(0f)] public float WanderRadius = 4f;
		[Tooltip("Minimum seconds spent idling between random wander destinations.")]
		[Min(0f)] public float WanderIdleMin = 1f;
		[Tooltip("Maximum seconds spent idling between random wander destinations.")]
		[Min(0f)] public float WanderIdleMax = 3f;

		[Header("Personal space")]
		[Tooltip("When a player comes within this distance the NPC stops dead and stands idle until they step back out, instead of walking into / through them. 0 disables.")]
		[Min(0f)] public float PersonalSpaceRadius = 2f;

		[Header("Pushable")]
		[Tooltip("While stopped for a nearby player, the NPC can still be physically shoved by players within this distance (stays in its idle pose while pushed). Should exceed NPC radius + player radius (~0.8m). 0 disables.")]
		[Min(0f)] public float PushRadius = 1.2f;
		[Tooltip("How fast a player shoves the NPC, m/s at full contact.")]
		[Min(0f)] public float PushStrength = 3.5f;

		[Header("Patrol")]
		public PatrolMode PatrolMode = PatrolMode.Loop;
		[Tooltip("Seconds the NPC pauses at each waypoint before moving on.")]
		[Min(0f)] public float WaypointWaitSeconds = 1f;

		[Header("Hostile (optional)")]
		[Tooltip("When true, the NPC aggros and chases the nearest living player in range, attacking with Attack. Leave off for friendly townsfolk.")]
		public bool Hostile = false;
		[Tooltip("Attack used while a target is within range. Required when Hostile is true. Also supplies the EffectiveRange used to decide when to swing.")]
		public CombatAction Attack;
		[Tooltip("Radius in which players are detected and chased.")]
		[Min(0f)] public float AggroRadius = 10f;
		[Tooltip("NavMeshAgent speed while chasing a target.")]
		[Min(0f)] public float ChaseSpeed = 4.5f;
		[Tooltip("Seconds the target must stay out of range (or out of the movement area) before the NPC gives up and returns home.")]
		[Min(0f)] public float ResetGraceSeconds = 3f;
		[Tooltip("How often (ticks) the chase destination is refreshed. Lower = more responsive, more cost.")]
		[Min(1)] public int ChaseRepathTicks = 5;
		[Tooltip("If true, the NPC abandons the chase when the target leaves its movement area (keeps it leashed to its zone).")]
		public bool LeashChaseToArea = true;

		[Header("Phase Awareness")]
		[Tooltip("Match phases during which this NPC should exist. Empty = always active. NpcSpawner spawns/despawns accordingly (e.g. [Day] for townsfolk).")]
		public MatchPhase[] ActivePhases;

		/// <summary>True if this NPC type should be present during <paramref name="phase"/>.</summary>
		public bool IsActiveInPhase(MatchPhase phase)
		{
			if (ActivePhases == null || ActivePhases.Length == 0) return true;
			for (int i = 0; i < ActivePhases.Length; i++)
			{
				if (ActivePhases[i] == phase) return true;
			}
			return false;
		}
	}
}
