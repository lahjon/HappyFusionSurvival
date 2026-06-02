using Fusion;
using Starter.Common.Crafting;
using Starter.Common.Interactions;
using Starter.Common.Inventory;
using Starter.Common.Quests;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked NPC / machine that takes a list of <see cref="QuestDefinition"/> offers and pays
	/// out items + money when a player hands in the requested items. First-come-first-serve: each
	/// offer slot stores the <see cref="PlayerRef"/> that claimed it, and subsequent attempts no-op.
	///
	/// Inherits <see cref="InteractableStation"/> for the IInteractable boilerplate + host-side range
	/// re-validation. On interact, hands off to the local <see cref="QuestSession"/> which drives the
	/// menu UI. Phase-gated to the Day phase via <see cref="MatchManager"/>.
	/// </summary>
	public sealed class QuestGiver : InteractableStation
	{
		/// <summary>Hard cap on offers per giver; mirrors the networked array capacity below.</summary>
		public const int MaxOffers = 8;

		[Header("Quests")]
		[Tooltip("Offers this giver exposes. Capped at MaxOffers — extras are ignored. Drop more givers for bigger boards.")]
		public QuestDefinition[] Offers;

		/// <summary>
		/// One slot per offer; <see cref="PlayerRef.None"/> = unclaimed. State authority is the only writer.
		/// Reads on every peer drive UI status (Available / Completed by you / Completed by someone else).
		/// </summary>
		[Networked, Capacity(MaxOffers), OnChangedRender(nameof(OnCompletedChangedRender))]
		public NetworkArray<PlayerRef> CompletedBy => default;

		public event System.Action CompletedChanged;

		public override bool CanInteract => IsAvailablePhase();
		public override string LockedReason => IsAvailablePhase() ? string.Empty : $"{DisplayName} is closed";

		protected override void OnInteract(InteractionScanner scanner)
		{
			var session = scanner.GetComponent<QuestSession>();
			if (session != null) session.TryOpen(this);
		}

		/// <summary>
		/// Opens this quest board for the local player. Safe to wire to a UnityEvent or
		/// trigger collider — no arguments required.
		/// </summary>
		public void OpenMenu()
		{
			var playerObj = Runner != null ? Runner.GetPlayerObject(Runner.LocalPlayer) : null;
			var session   = playerObj != null
				? playerObj.GetComponent<QuestSession>()
				: FindFirstObjectByType<QuestSession>();
			session?.TryOpen(this);
		}

		/// <summary>
		/// Number of authored offer slots (capped at <see cref="MaxOffers"/>). UI iterates [0, OfferCount).
		/// </summary>
		public int OfferCount => Offers == null ? 0 : Mathf.Min(Offers.Length, MaxOffers);

		public QuestDefinition GetOffer(int index)
		{
			if (Offers == null || index < 0 || index >= Offers.Length || index >= MaxOffers) return null;
			return Offers[index];
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestComplete(int offerIndex, RpcInfo info = default)
		{
			if (Runner == null) return;
			if (offerIndex < 0 || offerIndex >= OfferCount) return;
			if (!IsAvailablePhase()) return;

			if (CompletedBy[offerIndex] != PlayerRef.None) return;

			var offer = GetOffer(offerIndex);
			if (offer == null) return;

			var source = ResolveSource(info);
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			var inventory = playerObj.GetComponent<Inventory>();
			if (inventory == null) return;

			// Validate: requested items present, item rewards have room.
			if (offer.Requested != null)
			{
				for (int i = 0; i < offer.Requested.Length; i++)
				{
					var req = offer.Requested[i];
					if (req.Item == null || req.Count <= 0) continue;
					if (InventoryOps.CountItem(inventory.Slots, req.Item.Id) < req.Count) return;
				}
			}

			if (offer.ItemRewards != null)
			{
				for (int i = 0; i < offer.ItemRewards.Length; i++)
				{
					var rew = offer.ItemRewards[i];
					if (rew.Item == null || rew.Count <= 0) continue;
					if (InventoryOps.RoomFor(inventory.Slots, rew.Item.Id) < rew.Count) return;
				}
			}

			// Commit: consume requested, grant items, grant money, claim the offer slot.
			if (offer.Requested != null)
			{
				for (int i = 0; i < offer.Requested.Length; i++)
				{
					var req = offer.Requested[i];
					if (req.Item == null || req.Count <= 0) continue;
					InventoryOps.TryRemoveItem(inventory.Slots, req.Item.Id, req.Count);
				}
			}

			if (offer.ItemRewards != null)
			{
				for (int i = 0; i < offer.ItemRewards.Length; i++)
				{
					var rew = offer.ItemRewards[i];
					if (rew.Item == null || rew.Count <= 0) continue;
					inventory.TryAdd(rew.Item.Id, rew.Count);
				}
			}

			if (offer.MoneyReward > 0)
			{
				var player = playerObj.GetComponent<Player>();
				if (player != null) player.AddMoney(offer.MoneyReward);
			}

			CompletedBy.Set(offerIndex, source);
		}

		private static bool IsAvailablePhase()
		{
			// Quests are turn-in-able during Day (Town) only. Lobby is allowed so pre-match
			// testing works without a MatchManager driving phase transitions.
			var mm = MatchManager.Instance;
			if (mm == null) return true;
			return mm.Phase == MatchPhase.Day || mm.Phase == MatchPhase.Lobby;
		}

		private PlayerRef ResolveSource(RpcInfo info)
		{
			return info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
		}

		private void OnCompletedChangedRender()
		{
			CompletedChanged?.Invoke();
		}
	}
}
