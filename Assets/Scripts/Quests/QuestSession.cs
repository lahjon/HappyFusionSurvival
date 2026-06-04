using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "having a quest giver open". See <see cref="InteractableSession{TStation}"/>
	/// for the shared open/close/auto-close lifecycle — this subclass adds the turn-in forwarder, its own
	/// "is any open" flag, and the NPC attend hooks.
	/// </summary>
	public sealed class QuestSession : InteractableSession<QuestGiver>
	{
		/// <summary>True while ANY local QuestSession has an open giver. Polled by UI gates to suppress menu toggles during a turn-in.</summary>
		public static bool IsAnyOpen { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => IsAnyOpen = false;

		protected override string Label => "Quest";
		protected override void SetAnyOpen(bool value) => IsAnyOpen = value;

		// Tell the NPC (if it's an NpcAgent) to stop and face us during the turn-in.
		protected override void OnOpened(QuestGiver giver) => giver.GetComponent<NpcAgent>()?.LocalBeginAttending();
		protected override void OnClosed(QuestGiver giver) => giver.GetComponent<NpcAgent>()?.LocalEndAttending();

		/// <summary>Forward a turn-in request to the open giver's state authority. UI should pre-gate via inventory + claim state.</summary>
		public void RequestComplete(int offerIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestComplete(offerIndex);
		}
	}
}
