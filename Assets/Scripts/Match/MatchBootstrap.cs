namespace Starter.Shooter
{
	/// <summary>
	/// Plain-static hand-off from the networked lobby scene to the networked game scene. The chosen team size is
	/// host-only <i>input</i> — it does not need to replicate before the game scene exists, because the game scene's
	/// <see cref="TeamManager"/> turns it into networked state the instant the match begins. Carrying it through a
	/// static (which survives <c>Runner.LoadScene</c> on the host process) avoids needing a DontDestroyOnLoad
	/// NetworkObject just to ferry one int across the scene transition.
	///
	/// Written by <see cref="LobbyManager.StartMatch"/> (host) immediately before loading the game scene; read by
	/// <see cref="MatchManager"/> (host) when it auto-begins the round. Irrelevant on clients.
	/// </summary>
	public static class MatchBootstrap
	{
		/// <summary>Team size selected in the lobby (1=Solo, 2=Duo, 3=Trio). Defaults to Solo so a direct-from-game-scene
		/// start (skipping the lobby) puts every player on their own team — i.e. everyone is an enemy, for quick PvP testing.
		/// The lobby overwrites this the instant the host picks a size, so normal flow is unaffected.</summary>
		public static int PendingTeamSize = 1;
	}
}
