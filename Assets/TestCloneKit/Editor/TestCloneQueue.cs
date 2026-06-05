// Test Clone Kit — job queue runner. Drains jobs enqueued by the /clone-test skill (or any producer) into the clone.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TestCloneKit
{
	[Serializable] public class JobFile { public string path; public string op; } // op: "write" | "delete"

	[Serializable]
	public class JobManifest
	{
		public string id;
		public string label;
		public string session;
		public string createdAt;
		public string action = "play";   // "play" | "recompile" | "menu:<Menu/Path>"
		public JobFile[] files;
	}

	public class JobInfo
	{
		public string dir;
		public string name;
		public JobManifest manifest;
		public string Label => manifest != null && !string.IsNullOrEmpty(manifest.label) ? manifest.label : name;
		public int FileCount => manifest?.files?.Length ?? 0;
		public string Session => manifest?.session ?? "";
	}

	/// <summary>
	/// Watches the shared job queue (under the main worktree's <c>.clone-test-queue/</c>) and applies jobs into the clone:
	/// copy the snapshotted files in, recompile, then run the per-job action (default: enter Play). "Run Next" is manual;
	/// "Auto-drain" processes jobs hands-free as they arrive. Only runs meaningfully inside the clone (a linked worktree) —
	/// it never applies jobs to the main project. State survives domain reloads via <see cref="SessionState"/>.
	/// </summary>
	[InitializeOnLoad]
	public static class TestCloneQueue
	{
		private const string AutoKey = "TestCloneKit:autodrain";
		private const string PendingActionKey = "TestCloneKit.pendingAction"; // SessionState, survives reloads

		private static double _nextPoll;

		static TestCloneQueue()
		{
			EditorApplication.update += OnUpdate;
		}

		public static bool AutoDrain
		{
			get => EditorPrefs.GetBool(AutoKey, false);
			set => EditorPrefs.SetBool(AutoKey, value);
		}

		// ---------------------------------------------------------------------
		// Queue layout (shared via the main worktree)
		// ---------------------------------------------------------------------

		public static string QueueRoot() => Path.Combine(TestCloneService.MainWorktree(), ".clone-test-queue");
		private static string Pending() => Path.Combine(QueueRoot(), "pending");
		private static string Processing() => Path.Combine(QueueRoot(), "processing");
		private static string Done() => Path.Combine(QueueRoot(), "done");
		private static string Failed() => Path.Combine(QueueRoot(), "failed");

		public static List<JobInfo> PendingJobs()
		{
			var list = new List<JobInfo>();
			var p = Pending();
			if (!Directory.Exists(p)) return list;
			foreach (var dir in Directory.GetDirectories(p))
				list.Add(new JobInfo { dir = dir, name = Path.GetFileName(dir), manifest = ReadManifest(dir) });
			list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			return list;
		}

		public static int DoneCount() => CountDirs(Done());
		public static int FailedCount() => CountDirs(Failed());
		private static int CountDirs(string p) => Directory.Exists(p) ? Directory.GetDirectories(p).Length : 0;

		private static JobManifest ReadManifest(string dir)
		{
			try
			{
				var f = Path.Combine(dir, "manifest.json");
				if (File.Exists(f))
				{
					var m = JsonUtility.FromJson<JobManifest>(File.ReadAllText(f));
					if (m != null) { m.files ??= Array.Empty<JobFile>(); return m; }
				}
			}
			catch { /* fall through to placeholder */ }
			return new JobManifest { id = Path.GetFileName(dir), label = Path.GetFileName(dir), action = "play", files = Array.Empty<JobFile>() };
		}

		// ---------------------------------------------------------------------
		// Running
		// ---------------------------------------------------------------------

		/// <summary>Apply the next pending job into the clone, then queue its action to fire after the recompile.</summary>
		public static bool RunNext(out string message)
		{
			message = null;
			if (!TestCloneService.IsLinkedWorktree)
			{
				message = "Open the CLONE editor to drain the queue — the main project must stay untouched.";
				return false;
			}
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				message = "Editor is busy (compiling). Try again in a moment.";
				return false;
			}
			if (!string.IsNullOrEmpty(SessionState.GetString(PendingActionKey, "")))
			{
				message = "A job is already in progress.";
				return false;
			}

			var jobs = PendingJobs();
			if (jobs.Count == 0) { message = "Queue is empty."; return false; }
			var job = jobs[0];

			Directory.CreateDirectory(Processing());
			var procDir = Path.Combine(Processing(), job.name);
			MoveDir(job.dir, procDir);

			if (!Apply(procDir, job.manifest, out var applyErr))
			{
				Directory.CreateDirectory(Failed());
				MoveDir(procDir, Path.Combine(Failed(), job.name));
				message = $"Job {job.name} failed: {applyErr}";
				Debug.LogError("[TestClone] " + message);
				return false;
			}

			// Applied — archive to done/ now; the action fires once the editor settles after the import/reload.
			Directory.CreateDirectory(Done());
			MoveDir(procDir, Path.Combine(Done(), job.name));

			SessionState.SetString(PendingActionKey, job.manifest?.action ?? "play");
			AssetDatabase.Refresh();
			message = $"Applied job {job.name} ({job.FileCount} file(s)).";
			Debug.Log("[TestClone] " + message);
			return true;
		}

		private static bool Apply(string jobDir, JobManifest m, out string error)
		{
			error = null;
			try
			{
				var baseDir = TestCloneService.RepoRoot(); // the clone's own root
				var filesDir = Path.Combine(jobDir, "files");
				if (m?.files != null)
					foreach (var f in m.files)
					{
						var dst = Path.Combine(baseDir, f.path);
						if (f.op == "delete")
						{
							if (File.Exists(dst)) File.Delete(dst);
							if (File.Exists(dst + ".meta")) File.Delete(dst + ".meta");
						}
						else
						{
							var src = Path.Combine(filesDir, f.path);
							if (!File.Exists(src)) { error = "missing snapshot for " + f.path; return false; }
							Directory.CreateDirectory(Path.GetDirectoryName(dst));
							File.Copy(src, dst, overwrite: true);
							if (File.Exists(src + ".meta")) File.Copy(src + ".meta", dst + ".meta", overwrite: true);
						}
					}
				return true;
			}
			catch (Exception e) { error = e.Message; return false; }
		}

		// ---------------------------------------------------------------------
		// Tick: fire deferred actions + auto-drain
		// ---------------------------------------------------------------------

		private static void OnUpdate()
		{
			if (EditorApplication.timeSinceStartup < _nextPoll) return;
			_nextPoll = EditorApplication.timeSinceStartup + 1.0; // ~1 Hz is plenty

			// 1) A job was applied — fire its action once the editor is idle (compile/reload finished, not playing).
			var pending = SessionState.GetString(PendingActionKey, "");
			if (!string.IsNullOrEmpty(pending))
			{
				if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
				{
					SessionState.SetString(PendingActionKey, "");
					FireAction(pending);
				}
				return; // don't start another job until this one's action has resolved
			}

			// 2) Auto-drain the next job when idle (and not mid-test).
			if (!AutoDrain) return;
			if (!TestCloneService.IsLinkedWorktree) return;
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
			if (PendingJobs().Count == 0) return;
			RunNext(out _);
		}

		private static void FireAction(string action)
		{
			try
			{
				if (action == "play")
				{
					if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
				}
				else if (action != null && action.StartsWith("menu:", StringComparison.Ordinal))
				{
					var path = action.Substring(5).Trim();
					if (!string.IsNullOrEmpty(path)) EditorApplication.ExecuteMenuItem(path);
				}
				// "recompile" (or anything else): nothing further — the files are in and compiled.
			}
			catch (Exception e)
			{
				Debug.LogError("[TestClone] Action '" + action + "' failed: " + e.Message);
			}
		}

		// ---------------------------------------------------------------------

		private static void MoveDir(string from, string to)
		{
			if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
			Directory.CreateDirectory(Path.GetDirectoryName(to));
			Directory.Move(from, to);
		}
	}
}
