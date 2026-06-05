// Test Clone Kit — standalone Unity editor plugin. No dependencies beyond UnityEngine/UnityEditor.
// Drop the TestCloneKit folder into any project (Assets/ or Packages/) and it works as-is.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TestCloneKit
{
	/// <summary>One file changed in the working tree, as parsed from <c>git status --porcelain</c>.</summary>
	public class ChangeEntry
	{
		public bool Sync = true;
		public string Status;   // porcelain code, e.g. "M", "??", "R", "D"
		public string Path;     // repo-root-relative path (new path for renames)
		public string OldPath;  // set only for renames
		public bool Deleted;    // true when the file is gone from the working tree
	}

	/// <summary>
	/// All git / filesystem / settings logic for the Test Clone window. Project-agnostic: it discovers the git repo via
	/// <c>git rev-parse</c> and works with any layout (Unity project at the repo root or in a subfolder). Settings live in
	/// EditorPrefs keyed by repo path, so they never touch the project or get committed.
	/// </summary>
	public static class TestCloneService
	{
		private static string _repoRootCache;

		// =====================================================================
		// Paths
		// =====================================================================

		/// <summary>The Unity project root (folder containing Assets/).</summary>
		public static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

		/// <summary>The git repository top level, or null if this project isn't in a git repo.</summary>
		public static string RepoRoot()
		{
			if (_repoRootCache != null) return _repoRootCache.Length == 0 ? null : _repoRootCache;
			var (code, outp, _) = Run("git", "rev-parse --show-toplevel", ProjectRoot());
			_repoRootCache = code == 0 ? Path.GetFullPath(outp.Trim()) : "";
			return _repoRootCache.Length == 0 ? null : _repoRootCache;
		}

		public static bool HasGit => RepoRoot() != null;

		/// <summary>True when the *current* project is itself a linked worktree (i.e. this editor is the clone). A linked
		/// worktree has a <c>.git</c> file pointer; the main checkout has a <c>.git</c> directory.</summary>
		public static bool IsLinkedWorktree => HasGit && File.Exists(Path.Combine(RepoRoot(), ".git"));

		/// <summary>The main (non-linked) worktree path — the same value whether queried from the main editor or the clone.
		/// This is where the shared job queue lives, so both sides agree on one location.</summary>
		public static string MainWorktree()
		{
			var (code, outp, _) = RunGit("worktree list --porcelain");
			if (code == 0)
				foreach (var raw in outp.Split('\n'))
				{
					var line = raw.TrimEnd('\r');
					if (line.StartsWith("worktree ", StringComparison.Ordinal))
						return Path.GetFullPath(line.Substring("worktree ".Length).Trim());
				}
			return RepoRoot() ?? ProjectRoot();
		}

		/// <summary>Path of the Unity project relative to the repo root (empty when they're the same folder).</summary>
		public static string ProjectRelative()
		{
			var root = RepoRoot();
			if (root == null) return "";
			var proj = ProjectRoot();
			if (proj.Length <= root.Length) return "";
			return proj.Substring(root.Length).Trim(Path.DirectorySeparatorChar, '/', '\\');
		}

		// =====================================================================
		// Settings (EditorPrefs, per-repo, never committed)
		// =====================================================================

		private static string Key(string suffix) => "TestCloneKit:" + (RepoRoot() ?? ProjectRoot()) + ":" + suffix;

		public static string DefaultClonePath()
		{
			var root = (RepoRoot() ?? ProjectRoot()).TrimEnd('\\', '/');
			return root + "-TestClone";
		}

		public static string ClonePath
		{
			get => EditorPrefs.GetString(Key("path"), DefaultClonePath());
			set => EditorPrefs.SetString(Key("path"), value ?? "");
		}

		public static string Branch
		{
			get => EditorPrefs.GetString(Key("branch"), "test-clone");
			set => EditorPrefs.SetString(Key("branch"), string.IsNullOrEmpty(value) ? "test-clone" : value);
		}

		// =====================================================================
		// Status
		// =====================================================================

		public static bool CloneExists => !string.IsNullOrEmpty(ClonePath) && Directory.Exists(ClonePath);

		/// <summary>A linked worktree has a <c>.git</c> *file* (a pointer), not a directory.</summary>
		public static bool IsWorktree => CloneExists && File.Exists(Path.Combine(ClonePath, ".git"));

		public static string StatusLine =>
			!HasGit ? "this project is not inside a git repository"
			: string.IsNullOrEmpty(ClonePath) ? "no clone path set"
			: !CloneExists ? "not created"
			: IsWorktree ? "ready (worktree)"
			: "folder exists but is not a worktree";

		// =====================================================================
		// Lifecycle
		// =====================================================================

		public static bool CreateClone(out string error)
		{
			error = null;
			if (!HasGit) { error = "Not a git repository."; return false; }
			if (string.IsNullOrEmpty(ClonePath)) { error = "Set a clone folder first."; return false; }
			if (Directory.Exists(ClonePath)) { error = "Folder already exists: " + ClonePath; return false; }

			// -B creates-or-resets the branch to current HEAD, giving the clone a clean committed baseline. Uncommitted
			// work stays out until it's synced in deliberately.
			var (code, _, err) = RunGit($"worktree add -B {Branch} \"{ClonePath}\" HEAD");
			if (code != 0) { error = "git worktree add failed:\n" + err; return false; }
			return true;
		}

		public static bool RemoveClone(out string error)
		{
			error = null;
			var (code, _, err) = RunGit($"worktree remove \"{ClonePath}\" --force");
			if (code != 0) { error = "git worktree remove failed:\n" + err; return false; }
			return true;
		}

		public static bool OpenEditor(out string error)
		{
			error = null;
			if (!CloneExists) { error = "Create the clone first."; return false; }
			try
			{
				var rel = ProjectRelative();
				var projectInClone = string.IsNullOrEmpty(rel) ? ClonePath : Path.Combine(ClonePath, rel);
				Process.Start(new ProcessStartInfo
				{
					FileName = EditorApplication.applicationPath,
					Arguments = $"-projectPath \"{projectInClone}\"",
					UseShellExecute = false,
				});
				return true;
			}
			catch (Exception e) { error = e.Message; return false; }
		}

		// =====================================================================
		// Selective sync
		// =====================================================================

		public static List<ChangeEntry> RefreshChanges(out string error)
		{
			error = null;
			var list = new List<ChangeEntry>();
			if (!HasGit) { error = "Not a git repository."; return list; }

			var (code, outp, err) = RunGit("status --porcelain");
			if (code != 0) { error = "git status failed:\n" + err; return list; }

			var root = RepoRoot();
			foreach (var raw in outp.Split('\n'))
			{
				var line = raw.TrimEnd('\r');
				if (line.Length < 4) continue;

				var status = line.Substring(0, 2);
				var rest = line.Substring(3).Trim();

				string oldPath = null;
				var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
				if (arrow >= 0)
				{
					oldPath = Unquote(rest.Substring(0, arrow));
					rest = rest.Substring(arrow + 4);
				}
				var path = Unquote(rest);

				list.Add(new ChangeEntry
				{
					Sync = true,
					Status = status.Trim(),
					Path = path,
					OldPath = oldPath,
					Deleted = !File.Exists(Path.Combine(root, path)),
				});
			}
			return list;
		}

		/// <summary>Copy the selected files from this working tree into the clone (handles renames + deletions + .meta sidecars).</summary>
		public static (int copied, int deleted, int failed) Sync(IEnumerable<ChangeEntry> changes)
		{
			int copied = 0, deleted = 0, failed = 0;
			var root = RepoRoot();
			foreach (var c in changes)
			{
				if (!c.Sync) continue;
				try
				{
					if (!string.IsNullOrEmpty(c.OldPath))
					{
						DeleteInClone(c.OldPath);
						DeleteInClone(c.OldPath + ".meta");
					}

					if (c.Deleted)
					{
						if (DeleteInClone(c.Path)) deleted++;
						DeleteInClone(c.Path + ".meta");
					}
					else
					{
						CopyToClone(root, c.Path);
						copied++;
						var meta = c.Path + ".meta";
						if (File.Exists(Path.Combine(root, meta))) CopyToClone(root, meta);
					}
				}
				catch (Exception e)
				{
					failed++;
					Debug.LogError($"[TestClone] Sync failed for {c.Path}: {e.Message}");
				}
			}
			return (copied, deleted, failed);
		}

		// =====================================================================
		// Baseline drift — keep the clone's committed baseline in step with main
		// =====================================================================

		private static string ShortHead(string workDir)
		{
			var (code, outp, _) = Run("git", "rev-parse --short HEAD", workDir);
			return code == 0 ? outp.Trim() : "?";
		}

		public static string MainHead() => ShortHead(MainWorktree());
		public static string CloneHead() => CloneExists ? ShortHead(ClonePath) : "—";

		/// <summary>How many commits the clone's branch is behind the main worktree's HEAD (0 = baseline in sync).</summary>
		public static int CommitsBehind()
		{
			if (!CloneExists) return 0;
			var (c1, cloneCommit, _) = Run("git", "rev-parse HEAD", ClonePath);
			if (c1 != 0) return 0;
			var (c2, count, _) = Run("git", $"rev-list --count {cloneCommit.Trim()}..HEAD", MainWorktree());
			return c2 == 0 && int.TryParse(count.Trim(), out var n) ? n : 0;
		}

		/// <summary>Reset the clone's tracked files to main's current HEAD commit, so its baseline matches main again.
		/// Discards the clone's own committed/working tracked changes — caller must confirm + check for unsynced edits.</summary>
		public static bool Rebaseline(out string error)
		{
			error = null;
			if (!CloneExists) { error = "Clone doesn't exist."; return false; }
			var (c1, head, e1) = Run("git", "rev-parse HEAD", MainWorktree());
			if (c1 != 0) { error = "Could not read main HEAD:\n" + e1; return false; }
			var (c2, _, e2) = Run("git", $"reset --hard {head.Trim()}", ClonePath);
			if (c2 != 0) { error = "git reset failed in clone:\n" + e2; return false; }
			return true;
		}

		// =====================================================================
		// Reverse: pull changes made inside the clone back into the main working tree
		// =====================================================================

		/// <summary>List files changed inside the clone's own working tree (i.e. edits you made in the clone editor).</summary>
		public static List<ChangeEntry> RefreshCloneChanges(out string error)
		{
			error = null;
			var list = new List<ChangeEntry>();
			if (!CloneExists) { error = "Clone doesn't exist yet."; return list; }

			var (code, outp, err) = Run("git", "status --porcelain", ClonePath);
			if (code != 0) { error = "git status (clone) failed:\n" + err; return list; }

			foreach (var raw in outp.Split('\n'))
			{
				var line = raw.TrimEnd('\r');
				if (line.Length < 4) continue;

				var status = line.Substring(0, 2);
				var rest = line.Substring(3).Trim();

				string oldPath = null;
				var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
				if (arrow >= 0)
				{
					oldPath = Unquote(rest.Substring(0, arrow));
					rest = rest.Substring(arrow + 4);
				}
				var path = Unquote(rest);

				list.Add(new ChangeEntry
				{
					Sync = true,
					Status = status.Trim(),
					Path = path,
					OldPath = oldPath,
					Deleted = !File.Exists(Path.Combine(ClonePath, path)),
				});
			}
			return list;
		}

		/// <summary>Copy the selected clone changes back into the main working tree (uncommitted; you review + commit in main).</summary>
		public static (int copied, int deleted, int failed) PullFromClone(IEnumerable<ChangeEntry> changes)
		{
			int copied = 0, deleted = 0, failed = 0;
			var dstBase = RepoRoot(); // the main working tree (this editor)
			foreach (var c in changes)
			{
				if (!c.Sync) continue;
				try
				{
					if (!string.IsNullOrEmpty(c.OldPath))
					{
						DeleteAt(dstBase, c.OldPath);
						DeleteAt(dstBase, c.OldPath + ".meta");
					}

					if (c.Deleted)
					{
						if (DeleteAt(dstBase, c.Path)) deleted++;
						DeleteAt(dstBase, c.Path + ".meta");
					}
					else
					{
						CopyAcross(ClonePath, dstBase, c.Path);
						copied++;
						var meta = c.Path + ".meta";
						if (File.Exists(Path.Combine(ClonePath, meta))) CopyAcross(ClonePath, dstBase, meta);
					}
				}
				catch (Exception e)
				{
					failed++;
					Debug.LogError($"[TestClone] Pull failed for {c.Path}: {e.Message}");
				}
			}
			return (copied, deleted, failed);
		}

		// =====================================================================
		// Internals
		// =====================================================================

		private static void CopyAcross(string srcBase, string dstBase, string relPath)
		{
			var src = Path.Combine(srcBase, relPath);
			var dst = Path.Combine(dstBase, relPath);
			Directory.CreateDirectory(Path.GetDirectoryName(dst));
			File.Copy(src, dst, overwrite: true);
		}

		private static bool DeleteAt(string baseDir, string relPath)
		{
			var dst = Path.Combine(baseDir, relPath);
			if (!File.Exists(dst)) return false;
			File.Delete(dst);
			return true;
		}

		private static void CopyToClone(string repoRoot, string relPath)
		{
			var src = Path.Combine(repoRoot, relPath);
			var dst = Path.Combine(ClonePath, relPath);
			Directory.CreateDirectory(Path.GetDirectoryName(dst));
			File.Copy(src, dst, overwrite: true);
		}

		private static bool DeleteInClone(string relPath)
		{
			var dst = Path.Combine(ClonePath, relPath);
			if (!File.Exists(dst)) return false;
			File.Delete(dst);
			return true;
		}

		private static string Unquote(string s)
		{
			s = s.Trim();
			if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
				s = s.Substring(1, s.Length - 2);
			return s;
		}

		private static (int code, string stdout, string stderr) RunGit(string args) => Run("git", args, RepoRoot() ?? ProjectRoot());

		private static (int code, string stdout, string stderr) Run(string file, string args, string workDir)
		{
			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = file,
					Arguments = args,
					WorkingDirectory = workDir,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
					StandardOutputEncoding = Encoding.UTF8,
					StandardErrorEncoding = Encoding.UTF8,
				};
				using var p = Process.Start(psi);
				var outp = p.StandardOutput.ReadToEnd();
				var err = p.StandardError.ReadToEnd();
				p.WaitForExit(30000);
				return (p.ExitCode, outp, err);
			}
			catch (Exception e)
			{
				return (-1, "", e.Message);
			}
		}
	}
}
