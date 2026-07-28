using Fusion;
using Starter.Common.Interactions;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// Someone still working a stall in the dark who will sell you a location.
	///
	/// <para>The purchasable clue is the run's escape hatch from bad luck. Searching is the intended way to find
	/// a terminal, but a team that has swept three districts and found nothing should be able to convert what it
	/// <em>has</em> found — scavenged valuables — into progress. That keeps a cold run from becoming an hour of
	/// nothing happening.</para>
	/// </summary>
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class IntelVendor : NetworkBehaviour, IInteractable
	{
		[Header("Price")]
		[Tooltip("Item taken as payment. Usually the run's currency item.")]
		public ItemData PriceItem;

		[Tooltip("How many of PriceItem the intel costs.")]
		[Min(1)] public short Price = 5;

		public AudioClip SaleSound;

		/// <summary>Index into the run plan's stash list. Host writes it at spawn.</summary>
		[Networked] public int StashIndex { get; private set; }

		/// <summary>Raised locally when this peer buys, so the HUD can show what was bought.</summary>
		public static event System.Action<string> IntelPurchased;

		public void HostConfigure(StashSpec stash)
		{
			if (!HasStateAuthority) return;
			StashIndex = stash.Index;
		}

		private StashSpec Spec
		{
			get
			{
				var plan = RunDirector.Instance != null ? RunDirector.Instance.Plan : null;
				if (plan == null || StashIndex < 0 || StashIndex >= plan.Stashes.Count) return null;
				return plan.Stashes[StashIndex];
			}
		}

		public bool AlreadySold
		{
			get
			{
				var spec = Spec;
				if (spec == null || spec.Contents.IsNone) return true;
				return RunState.Instance != null && RunState.Instance.HasKnowledge(spec.Contents);
			}
		}

		// ── IInteractable ──────────────────────────────────────────────────────

		float IInteractable.InteractRange => 2.5f;
		bool IInteractable.CanInteract => !AlreadySold;
		Vector3 IInteractable.InteractionPoint => transform.position;

		string IInteractable.LockedReason =>
			AlreadySold ? "Nothing left to sell you" : null;

		string IInteractable.InteractLabel =>
			AlreadySold ? null : $"Buy intel ({Price})";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			if (AlreadySold) return;
			RPC_RequestPurchase(scanner.transform.position);
		}

		/// <summary>
		/// Payment is taken host-side. A client cannot be trusted to say it paid, and the item removal has to
		/// happen on the authority anyway since the inventory is networked state.
		/// </summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestPurchase(Vector3 playerPosition, RpcInfo info = default)
		{
			if (!HasStateAuthority) return;

			var spec = Spec;
			if (spec == null || spec.Contents.IsNone) return;
			if (Vector3.Distance(playerPosition, transform.position) > 4f) return;

			var buyer = Runner.GetPlayerObject(info.Source);
			var inventory = buyer != null ? buyer.GetComponent<Starter.Shooter.Inventory>() : null;
			if (inventory == null) return;

			if (PriceItem != null && PriceItem.Id != 0)
			{
				if (!InventoryOps.TryRemoveItem(inventory.Slots, PriceItem.Id, Price)) return;
			}

			RunState.Instance?.AuthorityGrantKnowledge(spec.Contents);
			RPC_ConfirmSale(spec.Text ?? spec.Contents.Describe());
		}

		[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
		private void RPC_ConfirmSale(string text)
		{
			IntelPurchased?.Invoke(text);
			if (SaleSound != null)
				Starter.Shooter.AudioManager.Instance?.PlaySFX(SaleSound, transform.position);
		}
	}
}
