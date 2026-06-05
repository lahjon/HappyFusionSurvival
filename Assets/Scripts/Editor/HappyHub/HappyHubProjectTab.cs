using System;
using System.IO;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// The Happy Hub <c>Project</c> tab — a landing page for project docs and key data assets. Scaffolded as the future
	/// home for at-a-glance project status (current phase design, links to prompt.md / CLAUDE.md, database shortcuts).
	/// </summary>
	[Serializable]
	public class HappyHubProjectTab
	{
		[InfoBox("Happy Hub — your go-to project & debug center. The Testing tab extends the Solo Play button: configure " +
			"a scenario (team size, bots, loadout, phase) and launch it in one click.", InfoMessageType.None)]
		[HorizontalGroup("docs"), Button("Open CLAUDE.md")]
		private void OpenClaudeMd() => OpenRepoFile("CLAUDE.md");

		[HorizontalGroup("docs"), Button("Open prompt.md")]
		private void OpenPromptMd() => OpenRepoFile("prompt.md");

		[Button("Reveal Test Config (UserSettings)")]
		private void RevealConfig()
		{
			var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", "HappyHub"));
			if (Directory.Exists(path))
				EditorUtility.RevealInFinder(path);
			else
				Debug.Log("[HappyHub] No saved test config yet — save one from the Testing tab first.");
		}

		// ---- MCP server ----

		// The MCP For Unity plugin's own EditorPref. Its [InitializeOnLoad] HttpAutoStartHandler reads this on every
		// editor launch and auto-starts the bridge/server when true — so toggling here is identical to flipping
		// "Auto-Start on Editor Load" in the plugin's Advanced Settings. Stored as a literal because the key lives in
		// the plugin's internal EditorPrefKeys class (not visible to this assembly). Requires HTTP transport enabled.
		private const string McpAutoStartKey = "MCPForUnity.AutoStartOnLoad";

		[TitleGroup("MCP Server")]
		[InfoBox("Requires the MCP For Unity HTTP transport. The plugin auto-starts the server only when HTTP transport " +
			"is selected — flip transport in its settings if auto-start doesn't kick in.", InfoMessageType.None,
			VisibleIf = nameof(AutoStartMcp))]
		[ShowInInspector, ToggleLeft, LabelText("Auto-Start MCP on Project Open")]
		[Tooltip("When enabled, the MCP For Unity server/bridge starts automatically every time you open this project.")]
		private bool AutoStartMcp
		{
			get => EditorPrefs.GetBool(McpAutoStartKey, false);
			set => EditorPrefs.SetBool(McpAutoStartKey, value);
		}

		[TitleGroup("MCP Server")]
		[Button("Open MCP For Unity Settings")]
		private void OpenMcpWindow()
		{
			if (!EditorApplication.ExecuteMenuItem("Window/MCP For Unity/Toggle MCP Window"))
				Debug.LogWarning("[HappyHub] Could not open the MCP For Unity window — is the plugin installed?");
		}

		private static void OpenRepoFile(string relative)
		{
			var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
			if (File.Exists(path))
				Application.OpenURL("file:///" + path.Replace('\\', '/'));
			else
				Debug.LogWarning($"[HappyHub] File not found: {relative}");
		}
	}
}
