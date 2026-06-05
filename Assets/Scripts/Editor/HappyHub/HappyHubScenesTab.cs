using System;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// The Happy Hub <c>Scenes</c> tab — quick openers for the two scenes in the build (00_MainMenu / HappyTown). Prompts
	/// to save the current scene first. Scaffolded for more scene shortcuts as the project grows.
	/// </summary>
	[Serializable]
	public class HappyHubScenesTab
	{
		private const string HappyTownScenePath = "Assets/Scenes/HappyTown.unity";
		private const string MainMenuScenePath = "Assets/Scenes/00_MainMenu/00_MainMenu.unity";

		[InfoBox("Opening a scene prompts to save the current one first.", InfoMessageType.None)]
		[DisableInPlayMode]
		[HorizontalGroup("row"), Button("Open HappyTown", ButtonSizes.Large)]
		private void OpenHappyTown() => Open(HappyTownScenePath);

		[DisableInPlayMode]
		[HorizontalGroup("row"), Button("Open Main Menu", ButtonSizes.Large)]
		private void OpenMainMenu() => Open(MainMenuScenePath);

		[Button("Open Build Settings")]
		private void OpenBuildSettings() => EditorApplication.ExecuteMenuItem("File/Build Settings...");

		private static void Open(string path)
		{
			if (EditorApplication.isPlaying)
			{
				EditorApplication.isPlaying = false;
				return;
			}
			if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
		}
	}
}
