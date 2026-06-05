// Test Clone Kit — standalone Unity editor plugin. No dependencies beyond UnityEngine/UnityEditor.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TestCloneKit
{
	/// <summary>
	/// Test Clone Kit — open a second, isolated Unity session (a git worktree) and copy only the changes you choose into
	/// it. Lets you play-test one thing in isolation while other tools/agents keep editing the main project. Plain IMGUI,
	/// no third-party dependencies; <see cref="TestCloneService"/> does the git/filesystem work.
	/// </summary>
	public class TestCloneWindow : EditorWindow
	{
		private readonly List<ChangeEntry> _changes = new();
		private Vector2 _scroll;
		private string _message;
		private MessageType _messageType = MessageType.None;

		private readonly List<ChangeEntry> _cloneChanges = new();
		private Vector2 _cloneScroll;

		private List<JobInfo> _queue = new();
		private Vector2 _queueScroll;
		private double _nextQueuePoll;

		[MenuItem("Tools/Test Clone")]
		public static void Open()
		{
			var w = GetWindow<TestCloneWindow>();
			w.titleContent = new GUIContent("Test Clone");
			w.minSize = new Vector2(440, 480);
			w.Show();
		}

		private void OnGUI()
		{
			EditorGUILayout.Space(4);
			EditorGUILayout.HelpBox(
				"A separate Unity session for isolated play-testing.\n" +
				"1) Create the clone (one-time; first open does a full asset import).\n" +
				"2) Open it as a second editor.\n" +
				"3) Pick which of your current changes to copy in, hit Sync — the clone reimports on focus.",
				MessageType.Info);

			if (!TestCloneService.HasGit)
			{
				EditorGUILayout.HelpBox("This project is not inside a git repository, so a worktree clone can't be created.",
					MessageType.Warning);
				return;
			}

			DrawSettings();
			EditorGUILayout.Space(8);
			DrawLifecycle();
			EditorGUILayout.Space(8);
			DrawChanges();
			EditorGUILayout.Space(8);
			DrawBaseline();
			DrawPullBack();
			EditorGUILayout.Space(8);
			DrawQueue();

			if (!string.IsNullOrEmpty(_message))
			{
				EditorGUILayout.Space(6);
				EditorGUILayout.HelpBox(_message, _messageType);
			}
		}

		private void OnEnable() { RefreshQueue(); RefreshBaseline(); }
		private void OnFocus() { RefreshQueue(); RefreshBaseline(); }

		private void OnInspectorUpdate()
		{
			// Keep the queue list live-ish without re-reading the disk every repaint.
			if (EditorApplication.timeSinceStartup < _nextQueuePoll) return;
			_nextQueuePoll = EditorApplication.timeSinceStartup + 1.0;
			RefreshQueue();
			Repaint();
		}

		private void RefreshQueue() => _queue = TestCloneQueue.PendingJobs();

		// ---------------------------------------------------------------------

		private void DrawSettings()
		{
			EditorGUILayout.LabelField("Clone", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			var path = EditorGUILayout.TextField(new GUIContent("Clone Folder", "Where the git worktree is created (a sibling folder)."), TestCloneService.ClonePath);
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Browse…", GUILayout.Width(80)))
				{
					var picked = EditorUtility.SaveFolderPanel("Choose clone folder", System.IO.Path.GetDirectoryName(path), "");
					if (!string.IsNullOrEmpty(picked)) path = picked;
				}
				if (GUILayout.Button("Reset to default", GUILayout.Width(120)))
					path = TestCloneService.DefaultClonePath();
			}
			var branch = EditorGUILayout.TextField(new GUIContent("Clone Branch", "Branch the worktree checks out (created/reset on Create)."), TestCloneService.Branch);
			if (EditorGUI.EndChangeCheck())
			{
				TestCloneService.ClonePath = path;
				TestCloneService.Branch = branch;
			}

			EditorGUILayout.LabelField("Status", TestCloneService.StatusLine);
		}

		private void DrawLifecycle()
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(TestCloneService.CloneExists))
				{
					if (GUILayout.Button("Create Clone", GUILayout.Height(26)))
					{
						if (TestCloneService.CreateClone(out var err))
							SetMessage($"Created at {TestCloneService.ClonePath}. Open it to run the first import.", MessageType.Info);
						else
							SetMessage(err, MessageType.Error);
					}
				}
				using (new EditorGUI.DisabledScope(!TestCloneService.CloneExists))
				{
					if (GUILayout.Button("Open in Unity", GUILayout.Height(26)))
					{
						if (TestCloneService.OpenEditor(out var err))
							SetMessage("Launching the clone editor. First open imports all assets — give it a minute.", MessageType.Info);
						else
							SetMessage(err, MessageType.Error);
					}
					if (GUILayout.Button("Remove Clone", GUILayout.Height(26)))
					{
						if (EditorUtility.DisplayDialog("Remove test clone?",
							$"Delete the worktree at:\n{TestCloneService.ClonePath}\n\nThe branch '{TestCloneService.Branch}' is kept. Continue?",
							"Remove", "Cancel"))
						{
							if (TestCloneService.RemoveClone(out var err))
							{
								_changes.Clear();
								SetMessage("Test clone removed.", MessageType.Info);
							}
							else SetMessage(err, MessageType.Error);
						}
					}
				}
			}
		}

		private void DrawChanges()
		{
			EditorGUILayout.LabelField("Changes to sync", EditorStyles.boldLabel);

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Refresh Changes"))
				{
					_changes.Clear();
					_changes.AddRange(TestCloneService.RefreshChanges(out var err));
					SetMessage(err ?? (_changes.Count == 0 ? "Working tree is clean — nothing to sync." : null),
						err != null ? MessageType.Error : MessageType.Info);
				}
				using (new EditorGUI.DisabledScope(_changes.Count == 0))
				{
					if (GUILayout.Button("Select All", GUILayout.Width(90))) foreach (var c in _changes) c.Sync = true;
					if (GUILayout.Button("Select None", GUILayout.Width(90))) foreach (var c in _changes) c.Sync = false;
				}
			}

			if (_changes.Count > 0)
			{
				_scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(280));
				foreach (var c in _changes)
				{
					using (new EditorGUILayout.HorizontalScope())
					{
						c.Sync = EditorGUILayout.ToggleLeft(GUIContent.none, c.Sync, GUILayout.Width(20));
						GUILayout.Label(string.IsNullOrEmpty(c.Status) ? "?" : c.Status, EditorStyles.miniBoldLabel, GUILayout.Width(34));
						GUILayout.Label(c.Deleted ? c.Path + "  (deleted)" : c.Path, EditorStyles.label);
					}
				}
				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space(4);
				using (new EditorGUI.DisabledScope(!TestCloneService.CloneExists))
				{
					var prev = GUI.backgroundColor;
					GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
					if (GUILayout.Button("⤓  Sync Selected → Clone", GUILayout.Height(30)))
					{
						var (copied, deleted, failed) = TestCloneService.Sync(_changes);
						SetMessage($"Sync done — {copied} copied, {deleted} deleted" +
							(failed > 0 ? $", {failed} failed" : "") + ". The clone reimports when you focus its window.",
							failed > 0 ? MessageType.Warning : MessageType.Info);
					}
					GUI.backgroundColor = prev;
				}
				if (!TestCloneService.CloneExists)
					EditorGUILayout.HelpBox("Create the clone before syncing.", MessageType.None);
			}
		}

		private int _behind = -1;
		private string _cloneHead = "", _mainHead = "";

		private void RefreshBaseline()
		{
			if (TestCloneService.CloneExists && !TestCloneService.IsLinkedWorktree)
			{
				_behind = TestCloneService.CommitsBehind();
				_cloneHead = TestCloneService.CloneHead();
				_mainHead = TestCloneService.MainHead();
			}
			else _behind = -1;
		}

		private void DrawBaseline()
		{
			if (!TestCloneService.CloneExists || TestCloneService.IsLinkedWorktree) return;
			if (_behind < 0) RefreshBaseline();

			EditorGUILayout.LabelField("Baseline", EditorStyles.boldLabel);

			var inSync = _behind == 0;
			EditorGUILayout.HelpBox(inSync
				? $"Clone baseline matches main (both at {_mainHead}). Everything not explicitly synced is identical."
				: $"Clone is {_behind} commit(s) behind main (clone {_cloneHead} · main {_mainHead}). Committed work in main " +
				  "after the clone was made is NOT in the clone — re-baseline so tests reflect main.",
				inSync ? MessageType.None : MessageType.Warning);

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Refresh", GUILayout.Width(80))) RefreshBaseline();
				using (new EditorGUI.DisabledScope(inSync))
				{
					if (GUILayout.Button($"Re-baseline clone → main ({_mainHead})"))
					{
						// Guard: re-baseline discards the clone's own tracked changes. Warn if the clone has unsynced edits.
						var cloneEdits = TestCloneService.RefreshCloneChanges(out _);
						var extra = cloneEdits.Count > 0
							? $"\n\n⚠ The clone has {cloneEdits.Count} uncommitted change(s) that will be DISCARDED. Pull them back first if you want to keep them."
							: "";
						if (EditorUtility.DisplayDialog("Re-baseline clone?",
							$"Reset the clone's tracked files to main's HEAD ({_mainHead}) via git reset --hard.{extra}",
							"Re-baseline", "Cancel"))
						{
							if (TestCloneService.Rebaseline(out var err))
							{
								AssetDatabase.Refresh();
								SetMessage($"Clone re-baselined to {_mainHead}. Reopen/refresh the clone editor to import the updated baseline.", MessageType.Info);
								_cloneChanges.Clear();
							}
							else SetMessage(err, MessageType.Error);
							RefreshBaseline();
						}
					}
				}
			}
			EditorGUILayout.Space(8);
		}

		private void DrawPullBack()
		{
			// Only meaningful in the main editor, where ClonePath points at a real clone.
			if (!TestCloneService.CloneExists || TestCloneService.IsLinkedWorktree) return;

			EditorGUILayout.LabelField("Changes made in the clone", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"Pull edits you made inside the clone back into THIS project as uncommitted changes, then review and commit " +
				"them here. Pulling overwrites the matching files in main — do it when you're ready to integrate (consider " +
				"pausing agents first).", MessageType.None);

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Refresh from Clone"))
				{
					_cloneChanges.Clear();
					_cloneChanges.AddRange(TestCloneService.RefreshCloneChanges(out var err));
					SetMessage(err ?? (_cloneChanges.Count == 0 ? "Clone has no changes to pull." : null),
						err != null ? MessageType.Error : MessageType.Info);
				}
				using (new EditorGUI.DisabledScope(_cloneChanges.Count == 0))
				{
					if (GUILayout.Button("Select All", GUILayout.Width(90))) foreach (var c in _cloneChanges) c.Sync = true;
					if (GUILayout.Button("Select None", GUILayout.Width(90))) foreach (var c in _cloneChanges) c.Sync = false;
				}
			}

			if (_cloneChanges.Count == 0) return;

			_cloneScroll = EditorGUILayout.BeginScrollView(_cloneScroll, GUILayout.MinHeight(80), GUILayout.MaxHeight(200));
			foreach (var c in _cloneChanges)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					c.Sync = EditorGUILayout.ToggleLeft(GUIContent.none, c.Sync, GUILayout.Width(20));
					GUILayout.Label(string.IsNullOrEmpty(c.Status) ? "?" : c.Status, EditorStyles.miniBoldLabel, GUILayout.Width(34));
					GUILayout.Label(c.Deleted ? c.Path + "  (deleted)" : c.Path, EditorStyles.label);
				}
			}
			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space(4);
			var prev = GUI.backgroundColor;
			GUI.backgroundColor = new Color(0.95f, 0.8f, 0.45f);
			if (GUILayout.Button("⤒  Pull Selected → Main (working tree)", GUILayout.Height(30)))
			{
				var (copied, deleted, failed) = TestCloneService.PullFromClone(_cloneChanges);
				AssetDatabase.Refresh();
				SetMessage($"Pulled into main — {copied} copied, {deleted} deleted" +
					(failed > 0 ? $", {failed} failed" : "") + ". Review the diff and commit in main.",
					failed > 0 ? MessageType.Warning : MessageType.Info);
				_cloneChanges.Clear();
			}
			GUI.backgroundColor = prev;
		}

		private void DrawQueue()
		{
			EditorGUILayout.LabelField("Job queue", EditorStyles.boldLabel);

			var isClone = TestCloneService.IsLinkedWorktree;
			EditorGUILayout.HelpBox(isClone
				? "This editor is the clone — it drains the queue. 'Run Next' applies a job's files, recompiles, then runs its action (default: enter Play). 'Auto-drain' does it hands-free as jobs arrive."
				: "This is the MAIN project (queued jobs are listed below). Open the clone editor to actually run them — the main project is never touched by the queue.",
				isClone ? MessageType.None : MessageType.Info);

			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(!isClone))
				{
					var auto = EditorGUILayout.ToggleLeft(new GUIContent("Auto-drain", "Process the next job automatically whenever idle and not in Play mode."), TestCloneQueue.AutoDrain, GUILayout.Width(110));
					if (auto != TestCloneQueue.AutoDrain) TestCloneQueue.AutoDrain = auto;
				}
				GUILayout.FlexibleSpace();
				GUILayout.Label($"done {TestCloneQueue.DoneCount()} · failed {TestCloneQueue.FailedCount()}", EditorStyles.miniLabel);
				if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshQueue();
			}

			if (_queue.Count == 0)
			{
				EditorGUILayout.LabelField("Queue is empty.", EditorStyles.miniLabel);
			}
			else
			{
				_queueScroll = EditorGUILayout.BeginScrollView(_queueScroll, GUILayout.MinHeight(60), GUILayout.MaxHeight(160));
				for (var i = 0; i < _queue.Count; i++)
				{
					var j = _queue[i];
					using (new EditorGUILayout.HorizontalScope())
					{
						GUILayout.Label(i == 0 ? "▶" : "  ", GUILayout.Width(16));
						GUILayout.Label(j.Label, EditorStyles.label);
						GUILayout.FlexibleSpace();
						GUILayout.Label($"{j.FileCount} file(s) · {j.manifest?.action}" + (string.IsNullOrEmpty(j.Session) ? "" : $" · {j.Session}"), EditorStyles.miniLabel);
					}
				}
				EditorGUILayout.EndScrollView();
			}

			using (new EditorGUI.DisabledScope(!isClone || _queue.Count == 0))
			{
				if (GUILayout.Button($"▶  Run Next  ({_queue.Count} queued)", GUILayout.Height(28)))
				{
					if (TestCloneQueue.RunNext(out var msg))
						SetMessage(msg, MessageType.Info);
					else
						SetMessage(msg, MessageType.Warning);
					RefreshQueue();
				}
			}
		}

		private void SetMessage(string msg, MessageType type)
		{
			_message = msg;
			_messageType = type;
		}
	}
}
