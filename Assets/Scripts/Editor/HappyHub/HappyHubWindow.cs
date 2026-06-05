using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// Happy Hub — the project's single go-to editor window for testing, debugging and project shortcuts. Structured into
	/// Odin tabs; the headline feature is the <c>Testing</c> tab, which extends the Solo Play button into a full scenario
	/// launcher (team size, bots, starting loadout, phase, time-scale) applied at runtime by <see cref="HappyTestRunner"/>.
	///
	/// Each tab is a self-contained serializable object so new capability slots in by adding a field below — no plumbing.
	/// </summary>
	public class HappyHubWindow : OdinEditorWindow
	{
		private const string Tabs = "tabs";

		[MenuItem("Tools/Happy Hub %#h")]
		private static void Open()
		{
			var window = GetWindow<HappyHubWindow>();
			window.titleContent = new GUIContent("Happy ♥");
			window.minSize = new Vector2(420, 480);
			window.Show();
		}

		[TabGroup(Tabs, "Testing")]
		[HideLabel, InlineProperty]
		public HappyHubTestingTab Testing = new();

		[TabGroup(Tabs, "Debug")]
		[HideLabel, InlineProperty]
		public HappyHubDebugTab Debug = new();

		[TabGroup(Tabs, "Scenes")]
		[HideLabel, InlineProperty]
		public HappyHubScenesTab Scenes = new();

		[TabGroup(Tabs, "Validation")]
		[HideLabel, InlineProperty]
		public HappyHubValidationTab Validation = new();

		[TabGroup(Tabs, "Project")]
		[HideLabel, InlineProperty]
		public HappyHubProjectTab Project = new();

		/// <summary>Save the Testing tab's current (possibly unsaved) values to disk. Called before an external launch
		/// (e.g. the Solo toolbar button) so it picks up in-progress edits rather than only the last explicit Save.</summary>
		public void PersistTestingConfig() => Testing?.ToConfig().Save();
	}
}
