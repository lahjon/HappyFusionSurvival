using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Named sub-seed identifiers for <see cref="RunRng"/>. Every generation pass draws from its own
	/// stream so the passes are independent: re-rolling <see cref="Content"/> reshuffles loot, clues and
	/// barriers without moving a single wall, and re-rolling <see cref="Logic"/> reshuffles the objective
	/// graph without touching the city. That separation is the whole point — "the buildings can be placed
	/// but the content will change".
	/// </summary>
	public enum RngStream
	{
		/// <summary>Streets, districts, blocks, lots. The physical shape of the city.</summary>
		Layout = 0,

		/// <summary>Per-building footprints, floor counts, room partitions. Still physical, but derived per building.</summary>
		Structure = 1,

		/// <summary>Loot, props, clue notes, which rooms are furnished how. Re-rollable without moving geometry.</summary>
		Content = 2,

		/// <summary>Objective placement, barrier composition, key placement. The randomizer layer.</summary>
		Logic = 3,

		/// <summary>Monster types, spawn nests, patrol assignment.</summary>
		Monsters = 4,

		/// <summary>Player start areas for the run.</summary>
		Spawns = 5,
	}

	/// <summary>
	/// Deterministic, allocation-light RNG used by every procedural pass in the city.
	///
	/// <para>Two hard requirements drive the design:</para>
	/// <list type="bullet">
	/// <item><b>Peer agreement.</b> Every client generates the same city from the same run seed with zero
	/// geometry replicated over the network. That only holds if the generator never touches
	/// <see cref="UnityEngine.Random"/> (shared global state, mutated by anything else in the frame) and never
	/// depends on iteration order of a hash container.</item>
	/// <item><b>Locality.</b> A building must roll the same interior whether it is generated at match start or
	/// streamed in twenty minutes later, and regardless of which neighbours were built first. So RNGs are
	/// <em>derived</em> from a coordinate/id rather than drawn from one long shared sequence.</item>
	/// </list>
	///
	/// <para>Implementation is SplitMix64 (finalizer for derivation, stepping for the sequence). It is a well
	/// distributed, extremely cheap 64-bit mixer with no state beyond a single ulong, which makes it trivial to
	/// snapshot and re-derive. Not cryptographic — nothing here needs it.</para>
	/// </summary>
	public struct RunRng
	{
		private ulong _state;

		private const ulong Gamma = 0x9E3779B97F4A7C15UL;

		public RunRng(ulong seed)
		{
			_state = seed == 0UL ? Gamma : seed;
		}

		// =====================================================================
		// Derivation — build a child RNG from a parent seed + coordinates
		// =====================================================================

		/// <summary>Root RNG for a whole stream of a run. Identical on every peer given the same run seed.</summary>
		public static RunRng ForRun(int runSeed, RngStream stream)
		{
			ulong s = Mix(unchecked((ulong)(uint)runSeed) | ((ulong)(uint)((int)stream + 1) << 32));
			return new RunRng(s);
		}

		/// <summary>
		/// RNG for one addressable thing inside a stream — a lot, a building, a floor, a container.
		/// <paramref name="a"/>/<paramref name="b"/>/<paramref name="c"/> are just mixing inputs; pass whatever
		/// identifies the thing (grid coords, building index, floor index). The result depends only on those
		/// values, never on how many things were generated before it.
		/// </summary>
		public static RunRng For(int runSeed, RngStream stream, int a, int b = 0, int c = 0)
		{
			ulong s = unchecked((ulong)(uint)runSeed);
			s = Mix(s ^ ((ulong)(uint)((int)stream + 1) * Gamma));
			s = Mix(s ^ ((ulong)(uint)a * 0xBF58476D1CE4E5B9UL));
			s = Mix(s ^ ((ulong)(uint)b * 0x94D049BB133111EBUL));
			s = Mix(s ^ ((ulong)(uint)c * 0xD6E8FEB86659FD93UL));
			return new RunRng(s);
		}

		/// <summary>Fork a child RNG off this one without disturbing the parent sequence. Use when a pass hands
		/// work to a sub-pass and both must stay reproducible in isolation.</summary>
		public RunRng Fork(int salt)
		{
			return new RunRng(Mix(_state ^ ((ulong)(uint)salt * Gamma)));
		}

		/// <summary>Current internal state — snapshot it to resume an identical sequence later.</summary>
		public ulong State => _state;

		// =====================================================================
		// Draws
		// =====================================================================

		/// <summary>Next raw 64 bits.</summary>
		public ulong NextULong()
		{
			unchecked
			{
				_state += Gamma;
				return Mix(_state);
			}
		}

		public uint NextUInt() => (uint)(NextULong() >> 32);

		/// <summary>Uniform in [0, 1).</summary>
		public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

		/// <summary>Uniform in [min, max).</summary>
		public float Range(float min, float max) => min + (max - min) * NextFloat();

		/// <summary>Uniform integer in [minInclusive, maxExclusive). Returns <paramref name="minInclusive"/> when the
		/// range is empty, so callers never have to guard degenerate ranges.</summary>
		public int Range(int minInclusive, int maxExclusive)
		{
			if (maxExclusive <= minInclusive) return minInclusive;
			uint span = (uint)(maxExclusive - minInclusive);
			return minInclusive + (int)(NextUInt() % span);
		}

		/// <summary>Uniform integer in [0, count).</summary>
		public int Index(int count) => count <= 0 ? 0 : (int)(NextUInt() % (uint)count);

		/// <summary>True with probability <paramref name="chance"/> (0..1).</summary>
		public bool Chance(float chance) => NextFloat() < chance;

		/// <summary>Random point inside a rectangle (XZ).</summary>
		public Vector2 InRect(Rect r) => new Vector2(Range(r.xMin, r.xMax), Range(r.yMin, r.yMax));

		/// <summary>Uniform pick from a list. Returns <c>default</c> for an empty list.</summary>
		public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> items)
		{
			if (items == null || items.Count == 0) return default;
			return items[Index(items.Count)];
		}

		/// <summary>Index picked proportionally to <paramref name="weights"/>. Non-positive weights are skipped.
		/// Returns -1 when every weight is non-positive.</summary>
		public int PickWeighted(System.Collections.Generic.IReadOnlyList<float> weights)
		{
			if (weights == null || weights.Count == 0) return -1;

			float total = 0f;
			for (int i = 0; i < weights.Count; i++)
				if (weights[i] > 0f) total += weights[i];
			if (total <= 0f) return -1;

			float roll = NextFloat() * total;
			for (int i = 0; i < weights.Count; i++)
			{
				if (weights[i] <= 0f) continue;
				roll -= weights[i];
				if (roll <= 0f) return i;
			}
			// Float drift on the last bucket.
			for (int i = weights.Count - 1; i >= 0; i--)
				if (weights[i] > 0f) return i;
			return -1;
		}

		/// <summary>In-place Fisher–Yates. Deterministic given the RNG state.</summary>
		public void Shuffle<T>(System.Collections.Generic.IList<T> items)
		{
			if (items == null) return;
			for (int i = items.Count - 1; i > 0; i--)
			{
				int j = Index(i + 1);
				(items[i], items[j]) = (items[j], items[i]);
			}
		}

		// =====================================================================

		private static ulong Mix(ulong z)
		{
			unchecked
			{
				z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
				z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
				return z ^ (z >> 31);
			}
		}
	}
}
