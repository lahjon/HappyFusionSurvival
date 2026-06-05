using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// Adds two play buttons to the main editor toolbar's centre cluster, next to the standard Play controls:
	///
	///   • "Solo"      — enters Play mode with <see cref="UIGameMenu.ForceSinglePlayer"/> forced on (the
	///                    isolated-host path in <see cref="UIGameMenu.StartGame"/>), starting from whatever
	///                    scene is currently open.
	///   • "Main Menu" — enters Play mode starting from the <c>00_MainMenu</c> scene (the lobby), regardless of
	///                    which scene is open, via <see cref="EditorSceneManager.playModeStartScene"/>. The
	///                    open scene is left untouched, and the start-scene override is cleared on exit.
	///
	/// Neither path touches the regular Play button. The "Solo" force is wired via a <see cref="SessionState"/>
	/// flag that <see cref="UIGameMenu"/> ORs into its isolated-host check (so the serialized scene field is
	/// never written / the scene never dirtied), cleared again when Play mode exits.
	///
	/// Buttons are registered through Unity 6's supported <see cref="MainToolbarElementAttribute"/> API
	/// (Toolbars.MainToolbar). The toolbar owns the generated VisualElements, so we locate them by reflection
	/// to (a) match the grey pill / 2px radius of the Play/Pause/Step strip with a lighter hover, and
	/// (b) disable them during Play mode. Reflection injection into the toolbar tree is the "unsupported
	/// method" Unity now refuses to render — don't go back to it.
	/// </summary>
	[InitializeOnLoad]
	public static class SinglePlayerToolbarButton
	{
		// Must match the key read by UIGameMenu.EditorSinglePlayerOverride.
		private const string SessionKey = "HFS.ForceSinglePlayer";

		// Unique toolbar element ids. These are also the names of the generated overlay elements, and the
		// "HappyFusion" prefix is what SetDisplayedAll keys off to reveal both at once.
		private const string SoloPath = "HappyFusion/PlaySingle";
		private const string MainMenuPath = "HappyFusion/PlayMainMenu";
		private static readonly string[] AllPaths = { SoloPath, MainMenuPath };

		private const string MainMenuScenePath = "Assets/Scenes/00_MainMenu/00_MainMenu.unity";

		// Newly-registered MainToolbarElements default to hidden in the saved toolbar layout, so we show ours
		// once. Bump the version suffix whenever the set of buttons changes, so existing installs re-reveal the
		// new ones. We only force visibility the first time per version — a later manual hide is respected.
		private const string AutoShownPref = "HFS.Toolbar.Shown.v2";

		// SessionState markers for the Main Menu start-scene override (survive domain reloads).
		private const string MainMenuActiveKey = "HFS.MainMenu.Active";
		private const string MainMenuPrevSceneKey = "HFS.MainMenu.PrevScene";

		private static Type _mainToolbarType;
		private static int _styleRetries;

		static SinglePlayerToolbarButton()
		{
			EditorApplication.playModeStateChanged += OnPlayModeChanged;
			EditorApplication.delayCall += Initialize;
		}

		private static void Initialize()
		{
			EnsureDisplayedOnce();
			BeginStyleAndStateRetry();
		}

		// ---- Factories (discovered by Unity's main-toolbar system) ----

		/// <summary>
		/// <see cref="MainToolbarDockPosition.Middle"/> docks in the Play-controls cluster; the large dock
		/// index pushes these to the right of the built-in buttons (Main Menu just after Solo).
		/// </summary>
		[MainToolbarElement(SoloPath,
			defaultDockPosition = MainToolbarDockPosition.Middle,
			defaultDockIndex = 1000)]
		private static MainToolbarButton CreateSoloButton()
		{
			var icon = EditorGUIUtility.IconContent("PlayButton").image as Texture2D;
			var content = new MainToolbarContent("Solo", icon,
				"Play Single Player — enters Play mode with UIGameMenu.ForceSinglePlayer on (isolated host), applying the " +
				"Happy Hub Testing-tab scenario (team size, bots, loadout, phase).");
			return new MainToolbarButton(content, OnSoloClicked);
		}

		[MainToolbarElement(MainMenuPath,
			defaultDockPosition = MainToolbarDockPosition.Middle,
			defaultDockIndex = 1001)]
		private static MainToolbarButton CreateMainMenuButton()
		{
			var icon = EditorGUIUtility.IconContent("PlayButton").image as Texture2D;
			var content = new MainToolbarContent("Main Menu", icon,
				"Play from the 00_MainMenu scene (lobby), regardless of the open scene. Restores the open scene on exit.");
			return new MainToolbarButton(content, OnMainMenuClicked);
		}

		// ---- Click behaviour ----

		private static void OnSoloClicked()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;

			// Sanity-check that the scene actually has a UIGameMenu to react to the flag, so the button
			// doesn't silently do nothing on, say, the main-menu scene.
			var menus = UnityEngine.Object.FindObjectsByType<UIGameMenu>(FindObjectsInactive.Include);
			if (menus == null || menus.Length == 0)
			{
				EditorUtility.DisplayDialog("Play Single Player",
					"No UIGameMenu found in the loaded scene(s).\n\nOpen the gameplay scene (e.g. HappyTown) and try again.",
					"OK");
				return;
			}

			// Apply the Happy Hub Testing-tab scenario on top of the isolated-host launch (bots, loadout, phase, …).
			HappyTestLauncher.ArmTestConfig();

			SessionState.SetBool(SessionKey, true);
			EditorApplication.EnterPlaymode();
		}

		private static void OnMainMenuClicked()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;

			var startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath);
			if (startScene == null)
			{
				EditorUtility.DisplayDialog("Play from Main Menu",
					$"Couldn't find the main-menu scene at:\n{MainMenuScenePath}",
					"OK");
				return;
			}

			// Remember the current start-scene override (usually none) so we can restore it on exit, then point
			// Play mode at the main-menu scene without disturbing the open scene.
			var prev = EditorSceneManager.playModeStartScene;
			SessionState.SetString(MainMenuPrevSceneKey, prev != null ? AssetDatabase.GetAssetPath(prev) : "");
			SessionState.SetBool(MainMenuActiveKey, true);
			EditorSceneManager.playModeStartScene = startScene;
			EditorApplication.EnterPlaymode();
		}

		private static void OnPlayModeChanged(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.EnteredEditMode)
			{
				// Clear the Solo override so the next plain Play press is unaffected.
				SessionState.EraseBool(SessionKey);

				// Restore the previous Play-mode start scene if we overrode it for the Main Menu button.
				if (SessionState.GetBool(MainMenuActiveKey, false))
				{
					var prevPath = SessionState.GetString(MainMenuPrevSceneKey, "");
					EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(prevPath)
						? null
						: AssetDatabase.LoadAssetAtPath<SceneAsset>(prevPath);
					SessionState.EraseBool(MainMenuActiveKey);
					SessionState.EraseString(MainMenuPrevSceneKey);
				}
			}

			// Keep the disabled-while-playing state in sync across every transition; re-style on the way back
			// to edit mode in case the toolbar rebuilt the elements.
			UpdateEnabledState();
			if (state == PlayModeStateChange.EnteredEditMode)
				BeginStyleAndStateRetry();
		}

		// ---- Toolbar element lookup (reflection — the toolbar owns the VisualElements) ----

		private static Type MainToolbarType
		{
			get
			{
				if (_mainToolbarType == null)
				{
					foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
					{
						_mainToolbarType = asm.GetType("UnityEditor.Toolbars.MainToolbar");
						if (_mainToolbarType != null)
							break;
					}
				}
				return _mainToolbarType;
			}
		}

		private static EditorToolbarButton FindButtonElement(string path)
		{
			var prop = MainToolbarType?.GetProperty("window",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			var window = prop?.GetValue(null) as EditorWindow;
			var root = window != null ? window.rootVisualElement : null;
			return root?.Q(path)?.Q<EditorToolbarButton>();
		}

		// ---- Visibility (first run per version only) ----

		private static void EnsureDisplayedOnce()
		{
			if (EditorPrefs.GetBool(AutoShownPref, false))
				return;
			if (TryShowInToolbar())
				EditorPrefs.SetBool(AutoShownPref, true);
		}

		/// <summary>
		/// Marks our elements as displayed in the main toolbar layout. <c>MainToolbar.SetDisplayedAll</c> /
		/// <c>Refresh</c> are internal, so we call them by reflection; failure is non-fatal (the buttons can
		/// still be enabled manually from the toolbar's overflow "+" menu).
		/// </summary>
		private static bool TryShowInToolbar()
		{
			try
			{
				if (MainToolbarType == null)
					return false;

				const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
				MainToolbarType.GetMethod("SetDisplayedAll", flags)?.Invoke(null, new object[] { "HappyFusion", true });
				var refresh = MainToolbarType.GetMethod("Refresh", flags);
				foreach (var path in AllPaths)
					refresh?.Invoke(null, new object[] { path });
				return true;
			}
			catch
			{
				return false;
			}
		}

		// ---- Styling + enabled state ----

		// Idle/hover backgrounds. Idle matches the Play/Pause/Step toggles exactly (sampled from the live
		// toolbar); hover is a lighter grey, since setting an inline background suppresses the themed hover.
		private static Color IdleGrey => EditorGUIUtility.isProSkin
			? new Color(0.314f, 0.314f, 0.314f, 1f)
			: new Color(0.80f, 0.80f, 0.80f, 1f);

		private static Color HoverGrey => EditorGUIUtility.isProSkin
			? new Color(0.45f, 0.45f, 0.45f, 1f)
			: new Color(0.90f, 0.90f, 0.90f, 1f);

		/// <summary>
		/// The button elements don't exist until the toolbar has built them, which can be a few ticks after a
		/// domain reload, so we retry on delayCall until both appear (bounded, so we never loop forever).
		/// </summary>
		private static void BeginStyleAndStateRetry()
		{
			_styleRetries = 0;
			TryStyleAndState();
		}

		private static void TryStyleAndState()
		{
			if (ApplyStyleAndState())
				return;
			if (_styleRetries++ < 40)
				EditorApplication.delayCall += TryStyleAndState;
		}

		/// <summary>Styles every found button; returns true only once all buttons exist (keeps the retry going).</summary>
		private static bool ApplyStyleAndState()
		{
			bool allFound = true;
			bool playing = EditorApplication.isPlayingOrWillChangePlaymode;

			foreach (var path in AllPaths)
			{
				var button = FindButtonElement(path);
				if (button == null)
				{
					allFound = false;
					continue;
				}

				button.style.backgroundColor = IdleGrey;
				// Native toolbar toggles use a 2px radius — match it so the corners aren't over-rounded.
				button.style.borderTopLeftRadius = 2f;
				button.style.borderTopRightRadius = 2f;
				button.style.borderBottomLeftRadius = 2f;
				button.style.borderBottomRightRadius = 2f;
				button.style.paddingLeft = 6f;
				button.style.paddingRight = 6f;
				button.style.marginLeft = 2f;
				button.style.marginRight = 2f;

				// Re-register hover handlers idempotently (unregister first so re-styling doesn't stack them).
				button.UnregisterCallback<MouseEnterEvent>(OnButtonMouseEnter);
				button.UnregisterCallback<MouseLeaveEvent>(OnButtonMouseLeave);
				button.RegisterCallback<MouseEnterEvent>(OnButtonMouseEnter);
				button.RegisterCallback<MouseLeaveEvent>(OnButtonMouseLeave);

				button.SetEnabled(!playing);
			}

			return allFound;
		}

		private static void UpdateEnabledState()
		{
			bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
			foreach (var path in AllPaths)
				FindButtonElement(path)?.SetEnabled(!playing);
		}

		private static void OnButtonMouseEnter(MouseEnterEvent evt)
		{
			// Don't light up while disabled (Play mode).
			if (evt.currentTarget is VisualElement ve && ve.enabledSelf)
				ve.style.backgroundColor = HoverGrey;
		}

		private static void OnButtonMouseLeave(MouseLeaveEvent evt)
		{
			if (evt.currentTarget is VisualElement ve)
				ve.style.backgroundColor = IdleGrey;
		}
	}
}
