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
