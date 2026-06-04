using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "having a crafting bench open". See <see cref="InteractableSession{TStation}"/>
	/// for the shared open/close/auto-close lifecycle — this subclass only adds the bench-specific request
	/// forwarders and its own "is any open" flag.
	/// </summary>
	public sealed class CraftingSession : InteractableSession<CraftingBench>
	{
		/// <summary>True while ANY local CraftingSession has an open bench. Polled by UI gates to suppress menu toggles during crafting.</summary>
		public static bool IsAnyCrafting { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => IsAnyCrafting = false;

		protected override string Label => "Crafting";
		protected override void SetAnyOpen(bool value) => IsAnyCrafting = value;

		/// <summary>Forward a craft request to the open bench's state authority. UI should pre-gate via CanCraft.</summary>
		public void RequestCraft(int recipeId)
		{
			if (Current == null || recipeId == 0) return;
			Current.RPC_RequestCraft(recipeId);
		}

		/// <summary>Forward a scavenge request (destroy one unit of an inventory slot → scraps) to the open bench's state authority.</summary>
		public void RequestScavenge(int slotIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestScavenge(slotIndex);
		}
	}
}
