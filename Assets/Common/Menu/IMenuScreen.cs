namespace Starter.Common.Menu
{
	/// <summary>
	/// A local UI "screen" that participates in the global menu stack owned by <see cref="MenuManager"/>.
	/// Implemented by the pause menu and by every interaction session / modal panel (loot, crafting, quest,
	/// shop, computer, sleep, match lobby, match result). Registering on the stack is what lets the manager
	/// be the single owner of the Escape key instead of each screen polling the keyboard itself.
	/// </summary>
	public interface IMenuScreen
	{
		/// <summary>Short human-readable name, for logging/debugging the stack.</summary>
		string MenuName { get; }

		/// <summary>
		/// True if pressing Escape while this screen is on top should close it (via <see cref="CloseFromMenu"/>).
		/// False makes it a modal blocker: Escape is swallowed (does NOT fall through to open the pause menu),
		/// but the screen stays open and dismisses through its own logic (e.g. sleep wakes on Interact, the
		/// match lobby/result close when the networked phase changes).
		/// </summary>
		bool DismissOnEscape { get; }

		/// <summary>
		/// Perform this screen's own close. Must be idempotent — the manager only calls it for the top screen,
		/// but a screen may also close itself through other paths. The screen is responsible for calling
		/// <see cref="MenuManager.Close"/> as part of closing (typically it already does so in its close path).
		/// </summary>
		void CloseFromMenu();
	}
}
