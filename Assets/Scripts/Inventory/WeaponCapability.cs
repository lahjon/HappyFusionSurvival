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
	[Serializable]
	public sealed class WeaponCapability : ItemCapability
	{
		[Tooltip("Ordered list of actions this item can perform. Most weapons have one; the first is used by default.")]
		public List<CombatAction> Actions = new List<CombatAction>();

		[Tooltip("Visual-rig tuning the generic hand rig (HeldWeapon) reads at equip — swing/recoil/sway feel and muzzle.")]
		public HeldRigTuning Rig = new();
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
		public float AimRecoilLerpSpeed = 18f;

		[Header("Muzzle")]
		[Tooltip("Enable the muzzle flash (ranged weapons).")]
		public bool HasMuzzle = false;
		[Tooltip("Local position of the muzzle-flash anchor on the hand rig.")]
		public Vector3 MuzzleLocalPosition = Vector3.zero;
	}
}
