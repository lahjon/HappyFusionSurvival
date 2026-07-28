using System.Collections.Generic;

namespace Starter.Run
{
	/// <summary>
	/// Reachability logic for the randomizer. Two jobs: tell the planner what is reachable while it is placing
	/// items, and independently prove a finished plan is completable before the run ships.
	///
	/// <para>The second job is not redundant. Forward-fill placement is correct <em>by construction</em>, but only
	/// as long as every rule the placer used matches every rule the game enforces. Verifying the finished plan
	/// with a separate pass catches the case where those two drift apart — and an unwinnable run is the single
	/// worst thing this system can produce, because players will spend an hour looking for a key that is behind
	/// the door it opens.</para>
	/// </summary>
	public static class RunSolver
	{
		/// <summary>True when <paramref name="items"/> is enough to get through <paramref name="barrier"/>.</summary>
		public static bool IsOpen(BarrierSpec barrier, HashSet<RunItem> items)
		{
			if (barrier == null) return true;
			if (barrier.Kind.IsHardBlock()) return false;
			if (barrier.Requirement.IsNone) return true;
			if (items.Contains(barrier.Requirement)) return true;

			// Picking is the universal fallback on mechanical locks — deliberately, so a key shuffle can never
			// strand the team behind a deadbolt.
			return barrier.Kind.IsPickable() && items.Contains(new RunItem(RunItemKind.Lockpick));
		}

		/// <summary>True when every barrier on the route is passable.</summary>
		public static bool IsRouteOpen(RunPlanData plan, EntryRoute route, HashSet<RunItem> items)
		{
			for (int i = 0; i < route.BarrierIndices.Count; i++)
				if (!IsOpen(plan.Barrier(route.BarrierIndices[i]), items)) return false;
			return true;
		}

		/// <summary>
		/// Marks every region reachable with <paramref name="items"/> in hand. Runs to a fixed point because
		/// opening one site can reveal the stash holding the key to another.
		/// <paramref name="reachable"/> must be at least <see cref="RunPlanData.RegionCount"/> long.
		/// </summary>
		public static void ComputeReachable(RunPlanData plan, HashSet<RunItem> items, bool[] reachable)
		{
			for (int i = 0; i < reachable.Length; i++) reachable[i] = false;
			reachable[RunPlanData.StreetRegion] = true;

			bool changed = true;
			while (changed)
			{
				changed = false;

				for (int s = 0; s < plan.Sites.Count; s++)
				{
					var site = plan.Sites[s];
					int interior = plan.InteriorRegion(s);
					int target = plan.TargetRegion(s);

					if (!reachable[interior])
					{
						// Any single route is enough. This is where "the front door is welded this run, so climb"
						// becomes solvable rather than fatal.
						for (int r = 0; r < site.Routes.Count; r++)
						{
							if (!IsRouteOpen(plan, site.Routes[r], items)) continue;
							reachable[interior] = true;
							changed = true;
							break;
						}
					}

					if (reachable[interior] && !reachable[target])
					{
						bool allOpen = true;
						for (int b = 0; b < site.InteriorBarriers.Count; b++)
						{
							if (IsOpen(plan.Barrier(site.InteriorBarriers[b]), items)) continue;
							allOpen = false;
							break;
						}
						if (allOpen)
						{
							reachable[target] = true;
							changed = true;
						}
					}
				}
			}
		}

		/// <summary>
		/// Simulates a perfect team: take everything reachable, re-check what that opens, repeat. Returns true
		/// when all four objective terminals end up reachable.
		/// </summary>
		public static bool Verify(RunPlanData plan, out string failure)
		{
			var items = new HashSet<RunItem>();
			var reachable = new bool[plan.RegionCount];
			var taken = new bool[plan.Stashes.Count];

			bool progressed = true;
			while (progressed)
			{
				progressed = false;
				ComputeReachable(plan, items, reachable);

				for (int i = 0; i < plan.Stashes.Count; i++)
				{
					if (taken[i]) continue;
					var stash = plan.Stashes[i];
					if (stash.RegionId < 0 || stash.RegionId >= reachable.Length) continue;
					if (!reachable[stash.RegionId]) continue;

					taken[i] = true;
					if (!stash.Contents.IsNone && items.Add(stash.Contents))
						progressed = true;
				}
			}

			ComputeReachable(plan, items, reachable);

			for (int i = 0; i < plan.ObjectiveSites.Count; i++)
			{
				int siteIndex = plan.ObjectiveSites[i];
				if (reachable[plan.TargetRegion(siteIndex)]) continue;

				var site = plan.Site(siteIndex);
				failure = $"objective {i} at {(site != null ? site.Address : "?")} is unreachable";
				return false;
			}

			// Knowing where a terminal is should never be strictly required to finish — players can always sweep
			// the city the hard way — but a run where no intel exists at all is a bad run, so flag it.
			for (int i = 0; i < plan.ObjectiveSites.Count; i++)
			{
				if (HasIntelFor(plan, i)) continue;
				failure = $"no intel placed for objective {i}";
				return false;
			}

			failure = null;
			return true;
		}

		private static bool HasIntelFor(RunPlanData plan, int objectiveOrdinal)
		{
			var want = new RunItem(RunItemKind.Intel, objectiveOrdinal);
			for (int i = 0; i < plan.Stashes.Count; i++)
				if (plan.Stashes[i].Contents.Equals(want)) return true;
			return false;
		}

		/// <summary>Every distinct item the plan's barriers demand. This is the set the placer has to satisfy.</summary>
		public static void CollectRequiredItems(RunPlanData plan, List<RunItem> results)
		{
			results.Clear();
			for (int i = 0; i < plan.Barriers.Count; i++)
			{
				var req = plan.Barriers[i].Requirement;
				if (req.IsNone) continue;
				if (results.Contains(req)) continue;
				results.Add(req);
			}
		}
	}
}
