using UnityEngine;

namespace Starter.Common
{
	/// <summary>
	/// Process-wide world-generation seed. Set by the mode's GameManager early
	/// (typically in Awake) so scene-placed NetworkBehaviours can read a stable
	/// seed during their Spawned() callback. Clients receive the host's seed
	/// via a [Networked] mirror on GameManager for debug/UI parity; the loot
	/// roll itself only ever runs on state authority so clients don't need it.
	/// </summary>
	public static class WorldGen
	{
		public static int Seed { get; set; }

		/// <summary>
		/// Deterministic per-node RNG. Quantizes position to mm and folds it
		/// into the world seed so two nodes with the same seed roll
		/// independently and reordered scene init doesn't shift outcomes.
		/// </summary>
		public static System.Random RngFor(Vector3 worldPosition)
		{
			int x = Mathf.RoundToInt(worldPosition.x * 1000f);
			int y = Mathf.RoundToInt(worldPosition.y * 1000f);
			int z = Mathf.RoundToInt(worldPosition.z * 1000f);
			int h = unchecked(Seed * 397);
			h = unchecked((h ^ x) * 397);
			h = unchecked((h ^ y) * 397);
			h = unchecked(h ^ z);
			if (h == 0) h = 1;
			return new System.Random(h);
		}
	}
}
