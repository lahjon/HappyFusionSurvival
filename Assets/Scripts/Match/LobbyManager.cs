using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked state for the pre-match lobby scene (01_Lobby). Owns the host-selected team size and a registry of
	/// connected players' nicknames (the lobby has no spawned <see cref="Player"/> avatars to read names from, unlike
	/// the in-game roster). The host drives everything; clients read for display.
	///
	/// Scene-placed singleton — drop a NetworkObject in the main menu scene (00_MainMenu, which doubles as the lobby)
	/// with this on it. It despawns when the host leaves the lobby for the game scene; the chosen team size is ferried
	/// across via <see cref="MatchBootstrap"/>.
	/// </summary>
	public sealed class LobbyManager : NetworkBehaviour, IPlayerLeft
	{
		public static LobbyManager Instance { get; private set; }

		public const int MaxPlayers = TeamManager.MaxPlayers;

		[Header("Scene")]
		[Tooltip("Build index of the game scene to load when the host starts the match.")]
		public int GameSceneIndex = 1;

		[Header("Defaults")]
		[Range(TeamManager.MinTeamSize, TeamManager.MaxTeamSize)]
		public int DefaultTeamSize = 2;

		// =========================================================================
		// Networked state
		// =========================================================================

		/// <summary>1=Solo, 2=Duo, 3=Trio. Host writes via <see cref="SetTeamSize"/>; every client reads for the preview.</summary>
		[Networked] public int TeamSize { get; private set; }

		/// <summary>Host-only: set once <see cref="StartMatch"/> kicks off the game-scene load, so a second Start click
		/// can't fire another LoadScene. The menu busy overlay reads this to lock interaction during the transition.</summary>
		public bool IsStarting { get; private set; }

		/// <summary>PlayerRef → nickname. Each client reports its own name on spawn; the host owns the dictionary.</summary>
		[Networked, Capacity(MaxPlayers)]
		public NetworkDictionary<PlayerRef, NetworkString<_32>> Names => default;

		// =========================================================================
		// Lifecycle
		// =========================================================================

		public override void Spawned()
		{
			Instance = this;

			if (HasStateAuthority && TeamSize == 0)
				TeamSize = Mathf.Clamp(DefaultTeamSize, TeamManager.MinTeamSize, TeamManager.MaxTeamSize);

			// Report this peer's nickname to the host so the roster can show real names. Mirrors the
			// Player.RPC_SetNickname pattern used in the game scene.
			var nickname = PlayerPrefs.GetString("PlayerName");
			RPC_SetName(Runner.LocalPlayer, nickname);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		public void PlayerLeft(PlayerRef player)
		{
			if (HasStateAuthority == false) return;
			if (Names.ContainsKey(player))
				Names.Remove(player);
		}

		// =========================================================================
		// Host-driven mutations
		// =========================================================================

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_SetName(PlayerRef who, string nickname)
		{
			if (HasStateAuthority == false) return;
			if (string.IsNullOrEmpty(nickname))
				nickname = "Player" + who.PlayerId;
			Names.Set(who, nickname);
		}

		/// <summary>Host picks team size in the lobby. Clamped to [1, 3].</summary>
		public void SetTeamSize(int size)
		{
			if (HasStateAuthority == false) return;
			TeamSize = Mathf.Clamp(size, TeamManager.MinTeamSize, TeamManager.MaxTeamSize);
		}

		/// <summary>Host-only: lock in the team size and load the game scene for everyone. Clients follow the host's
		/// scene change automatically. No-op for clients or if no players are present.</summary>
		public void StartMatch()
		{
			if (HasStateAuthority == false) return;
			if (IsStarting) return; // already loading the game scene — ignore repeat clicks
			if (Runner.ActivePlayers.Count() <= 0) return;

			IsStarting = true;

			// Show the loading overlay the instant Start is clicked, before the network scene-load begins, so there is
			// no frozen frame between the click and OnSceneLoadStart. The runner's scene-load events take over from here
			// (and drive the overlay on every client too); it hides once the game scene is loaded.
			LoadingScreen.Instance?.Show("Loading match");

			// Hand the chosen size to the game scene's MatchManager (it has no networked link back to this scene).
			MatchBootstrap.PendingTeamSize = Mathf.Clamp(TeamSize, TeamManager.MinTeamSize, TeamManager.MaxTeamSize);

			Runner.LoadScene(SceneRef.FromIndex(GameSceneIndex), LoadSceneMode.Single, LocalPhysicsMode.None, true);
		}

		// =========================================================================
		// Read helpers (UI)
		// =========================================================================

		public string NameOf(PlayerRef player)
		{
			if (Names.TryGet(player, out var ns))
			{
				var s = ns.ToString();
				if (string.IsNullOrEmpty(s) == false) return s;
			}
			return "Player" + player.PlayerId;
		}
	}
}
