using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// Editor-only launch helper for the Happy Hub <c>Testing</c> tab. Extends the Solo Play button: it forces the same
	/// isolated-host path (<c>HFS.ForceSinglePlayer</c>) <i>and</i> raises a second flag (<c>HFS.TestConfigActive</c>)
	/// that <see cref="HappyTestRunner"/> reads at runtime to apply the saved <see cref="HappyTestConfig"/> scenario.
	///
	/// Both flags live in <see cref="SessionState"/> (survive domain reloads, never dirty the scene) and are cleared the
	/// moment Play mode exits, so a normal Play press is unaffected. The chosen start scene is set via
	/// <see cref="EditorSceneManager.playModeStartScene"/> so the launch works regardless of which scene is open, and the
	/// previous override is restored on exit — the same approach as the "Main Menu" toolbar button.
	/// </summary>
	[InitializeOnLoad]
	public static class HappyTestLauncher
	{
		// Must match the keys read by HappyTestRunner / UIGameMenu.
		private const string ForceSinglePlayerKey = "HFS.ForceSinglePlayer";
		private const string TestActiveKey = "HFS.TestConfigActive";

		// SessionState markers for restoring the play-mode start scene on exit (survive domain reloads).
		private const string SceneOverrideActiveKey = "HFS.TestConfig.SceneActive";
		private const string PrevSceneKey = "HFS.TestConfig.PrevScene";

		private const string HappyTownScenePath = "Assets/Scenes/HappyTown.unity";
		private const string MainMenuScenePath = "Assets/Scenes/00_MainMenu/00_MainMenu.unity";

		static HappyTestLauncher()
		{
			EditorApplication.playModeStateChanged += OnPlayModeChanged;
		}

		/// <summary>Save the config and enter Play mode with the Testing scenario armed. Returns false (with a dialog)
		/// if it can't proceed.</summary>
		public static bool Launch(HappyTestConfig config)
		{
			if (config == null) return false;
			if (EditorApplication.isPlayingOrWillChangePlaymode) return false;

			config.Save();

			bool toHappyTown = config.StartScene == HappyTestConfig.StartSceneOption.HappyTown;
			var scenePath = toHappyTown ? HappyTownScenePath : MainMenuScenePath;

			var startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
			if (startScene == null)
			{
				EditorUtility.DisplayDialog("Happy Hub — Testing",
					$"Couldn't find the scene to launch:\n{scenePath}", "OK");
				return false;
			}

			// Remember the current start-scene override (usually none) so we can restore it on exit.
			var prev = EditorSceneManager.playModeStartScene;
			SessionState.SetString(PrevSceneKey, prev != null ? AssetDatabase.GetAssetPath(prev) : "");
			SessionState.SetBool(SceneOverrideActiveKey, true);
			EditorSceneManager.playModeStartScene = startScene;

			// HappyTown launches as an isolated host (Solo path). The Main Menu path goes through the normal lobby flow,
			// so it must NOT force single-player — but the test config still applies once the game scene loads.
			SessionState.SetBool(ForceSinglePlayerKey, toHappyTown);
			SessionState.SetBool(TestActiveKey, true);

			EditorApplication.EnterPlaymode();
			return true;
		}

		/// <summary>Arm the Testing-tab scenario for a launch driven by something other than this launcher (the Solo
		/// toolbar button). Persists any open hub window's current values first, then raises the runtime flag so
		/// <see cref="HappyTestRunner"/> applies the saved config. The flag is cleared on Play-mode exit by
		/// <see cref="OnPlayModeChanged"/>. Does not touch the start scene or single-player flag — the caller owns those.</summary>
		public static void ArmTestConfig()
		{
			foreach (var window in Resources.FindObjectsOfTypeAll<HappyHubWindow>())
				window.PersistTestingConfig();

			SessionState.SetBool(TestActiveKey, true);
		}

		private static void OnPlayModeChanged(PlayModeStateChange state)
		{
			if (state != PlayModeStateChange.EnteredEditMode) return;

			// Clear our flags so the next plain Play press is unaffected.
			SessionState.EraseBool(TestActiveKey);
			SessionState.EraseBool(ForceSinglePlayerKey);

			// Restore the previous play-mode start scene if we overrode it.
			if (SessionState.GetBool(SceneOverrideActiveKey, false))
			{
				var prevPath = SessionState.GetString(PrevSceneKey, "");
				EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(prevPath)
					? null
					: AssetDatabase.LoadAssetAtPath<SceneAsset>(prevPath);
				SessionState.EraseBool(SceneOverrideActiveKey);
				SessionState.EraseString(PrevSceneKey);
			}
		}
	}
}
