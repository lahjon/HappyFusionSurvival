using System;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Gadget facet for the radar/scanner device: a 360° sweep that pings nearby enemies as fading dots on
	/// a world-space disc rendered in the holder's hand. All tuning lives here on the item asset; the runtime
	/// behavior is <see cref="HandScanner"/>, attached to the generic hand at equip via <see cref="CreateRuntime"/>.
	/// </summary>
	[Serializable]
	public sealed class RadarGadgetCapability : GadgetCapability
	{
		[Header("Scan")]
		[Tooltip("Horizontal radius (m) the radar covers. Targets outside are ignored.")]
		public float ScanRange = 10f;
		[Tooltip("Seconds for one full 360° sweep.")]
		public float SweepDuration = 10f;
		[Tooltip("Seconds a ping stays visible after being triggered (alpha lerps to 0 over this).")]
		public float PingLifetime = 3f;
		[Tooltip("If false, same-team players are filtered out (TeamManager.SameTeam). Default off — radar pings only show enemies.")]
		public bool IncludeTeammates = false;

		[Header("Visual")]
		[Tooltip("Local-space position of the radar canvas relative to the hand instance root.")]
		public Vector3 RadarLocalOffset = new(0f, 0.5f, 0.2f);
		[Tooltip("Local-space euler rotation of the radar disc (degrees). Default flips it 180° on Y so the front face points back at the camera.")]
		public Vector3 RadarLocalEuler = new(50f, 180f, 0f);
		[Tooltip("Uniform local scale of the radar canvas. The canvas is 256 logical pixels wide; final world size = 256 * scale * parent.lossyScale.")]
		public float RadarLocalScale = 0.004f;

		[Header("Colors")]
		public Color DiscColor = new(0f, 0.18f, 0f, 0.85f);
		public Color RingColor = new(0.4f, 1f, 0.4f, 0.9f);
		public Color SweepColor = new(0.4f, 1f, 0.4f, 0.9f);
		public Color SelfColor = new(0.85f, 1f, 0.85f, 1f);
		public Color PingColor = new(1f, 0.15f, 0.15f, 1f);

		public override HeldGadget CreateRuntime(GameObject host)
		{
			var scanner = host.AddComponent<HandScanner>();
			scanner.Initialize(this);
			return scanner;
		}
	}
}
