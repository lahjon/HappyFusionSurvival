using System;
using System.Collections.Generic;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Item facet that makes an item usable as a weapon: the ordered <see cref="CombatAction"/>s it
	/// can perform (most weapons use <see cref="Actions"/>[0]). The held HandPrefab is now a pure
	/// visual rig (<see cref="IHeldVisual"/>); the actions live here on the item asset so a weapon is
	/// designed in one place instead of on a prefab two GUID-hops away.
	///
	/// Player.GetActiveAction reads Actions[0]; ActionInvoker.TryFire executes it on the per-actor
	/// runtime. The CombatActions themselves are shared ScriptableObjects, unchanged.
	/// </summary>
	/// <summary>
	/// What the secondary attack button (RMB) does for a weapon. <see cref="Attack"/> fires
	/// <see cref="WeaponCapability.SecondaryAction"/> on press (e.g. throw); <see cref="Aim"/> is a held
	/// ADS stance that zooms the FOV, steadies sway/recoil, and tightens hitscan spread while held.
	/// </summary>
	public enum ESecondaryMode : byte
	{
		None   = 0,
		Attack = 1,
		Aim    = 2,
	}

	[Serializable]
	public sealed class WeaponCapability : ItemCapability
	{
		[Tooltip("Ordered list of actions this item can perform. Most weapons have one; the first is used by default.")]
		public List<CombatAction> Actions = new List<CombatAction>();

		[Header("Secondary (RMB)")]
		[Tooltip("What right-mouse does with this weapon. Attack fires SecondaryAction on press; Aim is a held ADS stance.")]
		public ESecondaryMode SecondaryMode = ESecondaryMode.None;
		[Tooltip("Action fired by RMB when SecondaryMode == Attack (e.g. a throw ProjectileAction). Uses its own cooldown, independent of the primary fire.")]
		public CombatAction SecondaryAction;
		[Tooltip("ADS tuning used when SecondaryMode == Aim.")]
		public AimTuning Aim = new();

		[Header("Ammo (magazine-fed)")]
		[Tooltip("Ammo item this weapon consumes. Null = no ammo (infinite — melee, fists). When set together with MagazineSize > 0 the weapon is magazine-fed: firing draws from the loaded magazine and auto-reloads from this item's inventory stacks when empty. (Projectile weapons like the Bow keep their own ProjectileAction.AmmoItem and ignore this.)")]
		public ItemData AmmoItem;
		[Tooltip("Rounds held in one magazine. 0 = not magazine-fed (no reload). >0 enables the magazine + auto-reload cycle for this hitscan weapon.")]
		[Min(0)]
		public int MagazineSize = 0;
		[Tooltip("Seconds to refill the magazine from reserve ammo once it runs dry.")]
		[Min(0f)]
		public float ReloadSeconds = 1.5f;

		/// <summary>True if this weapon uses the magazine + auto-reload cycle (a non-null ammo item and a positive magazine).</summary>
		public bool UsesMagazine => AmmoItem != null && MagazineSize > 0;

		[Tooltip("Visual-rig tuning the generic hand rig (HeldWeapon) reads at equip — swing/recoil/sway feel and muzzle.")]
		public HeldRigTuning Rig = new();
	}

	/// <summary>
	/// Per-weapon tuning for the held aim (ADS) stance — used when <see cref="WeaponCapability.SecondaryMode"/>
	/// is <see cref="ESecondaryMode.Aim"/>. FOV zoom is local-only cosmetic; the sway/recoil multipliers ride
	/// the existing networked LookRotation pipeline so all peers see the steadied aim.
	/// </summary>
	[Serializable]
	public sealed class AimTuning
	{
		[Tooltip("Degrees subtracted from the camera's base FOV while aiming (zoom in).")]
		public float FOVReduction = 18f;
		[Tooltip("How quickly the FOV eases toward the aimed value when RMB is pressed/released.")]
		public float FOVLerpSpeed = 10f;
		[Range(0f, 1f)]
		[Tooltip("Weapon-sway scale while aiming. 0 = perfectly steady, 1 = same as hip.")]
		public float SwayMultiplier = 0.25f;
		[Range(0f, 1f)]
		[Tooltip("Aim-recoil scale while aiming. Lower = flatter recoil when shooting from ADS.")]
		public float RecoilMultiplier = 0.5f;

		[Header("Breath-Hold (scoped — hold Sprint to steady)")]
		[Range(0f, 1f)]
		[Tooltip("Sway scale while holding the steady-aim (breath-hold) button. 0 = perfectly steady. Drains stamina fast (Player.BreathHoldStaminaDrainPerSecond).")]
		public float BreathHoldSwayMultiplier = 0f;
		[Tooltip("Recoil kick when stamina runs out mid breath-hold, as a fraction of a normal aim-recoil shot — the scope jolts as the breath gives out. 0 = no jolt.")]
		public float BreathDepletionRecoilScale = 0.6f;
	}

	/// <summary>
	/// Per-weapon tuning for the shared in-hand visual rig (<see cref="HeldWeapon"/>): swing/charge
	/// poses, ranged recoil, aim sway/recoil, and the muzzle-flash anchor. Moved off the per-weapon
	/// HandPrefab so a weapon is authored entirely on its item asset; the generic rig reads it at equip.
	/// </summary>
	[Serializable]
	public sealed class HeldRigTuning
	{
		[Header("Melee Swing")]
		public Vector3 SwingArcEuler = new Vector3(-80f, 0f, 0f);
		public float SwingDuration = 0.28f;

		[Header("Charged Attack Visuals")]
		[Range(0f, 1.5f)] public float ChargedBackMultiplier = 0.8f;
		public float ChargePoseLerpSpeed = 12f;
		public float ChargedSwingArcScale = 1.35f;
		public float ChargedSwingDurationScale = 1.2f;

		[Header("Recoil (ranged only)")]
		public float RecoilBackKick = 0.06f;
		public float RecoilPitchDegrees = -8f;
		public float RecoilDuration = 0.12f;

		[Header("Aim Sway (ranged only)")]
		[Range(0f, 1f)] public float WeaponSway = 0f;
		public float SwayMaxDegrees = 1.25f;
		public float SwayFrequency = 0.9f;

		[Header("Aim Recoil (ranged only, CS-style)")]
		[Range(0f, 1f)] public float AimRecoil = 0f;
		public float AimRecoilPitchPerShot = 3.5f;
		public float AimRecoilHorizontalRandom = 1.5f;
		[Tooltip("How fast the per-shot kick rises to its peak (higher = snappier).")]
		public float AimRecoilLerpSpeed = 18f;
		[Tooltip("How fast the accumulated recoil bleeds back toward zero after firing, returning the view to where it started (CS-style climb-then-settle). 0 = no recovery (kick stays).")]
		public float AimRecoilRecoverySpeed = 6f;

		[Header("Muzzle")]
		[Tooltip("Enable the muzzle flash (ranged weapons).")]
		public bool HasMuzzle = false;
		[Tooltip("Local position of the muzzle-flash anchor on the hand rig.")]
		public Vector3 MuzzleLocalPosition = Vector3.zero;
	}
}
