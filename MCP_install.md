# MCP_install.md

Setup for the **official Unity MCP** (shipped with `com.unity.ai.assistant`) so Claude Code can drive the Editor.

## 1. Remove any previous unity-mcp install

Old forks/community packages will collide with the official one. Wipe them before installing.

### Unity packages to remove
Open `Packages/manifest.json` and delete any of these dependency lines if present:
- `com.coplaydev.unity-mcp`
- `com.justinpbarnett.unity-mcp`
- Any other `*.unity-mcp` / `*.mcp-for-unity` entry

Then clear their package caches:
```powershell
Remove-Item -Recurse -Force "Library\PackageCache\com.coplaydev.unity-mcp@*"      -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "Library\PackageCache\com.justinpbarnett.unity-mcp@*" -ErrorAction SilentlyContinue
```

### Claude Code MCP server entries to remove
List what's registered, then drop any old unity entries:
```powershell
claude mcp list
claude mcp remove "unity-mcp"  -s user   # only if it points at a non-official binary
claude mcp remove "UnityMCP"   -s user
claude mcp remove "unityMCP"   -s user
claude mcp remove "unity"      -s user
```
Also scan `%USERPROFILE%\.claude.json` and any project `.claude/settings.json` / `.claude/settings.local.json` for stale `mcpServers` blocks pointing at Python servers (`uv run`, `python -m unity_mcp_server`, etc.) and delete those blocks.

### Old relay / Python server directories
```powershell
Remove-Item -Recurse -Force "$env:USERPROFILE\UnityMCP"   -ErrorAction SilentlyContinue   # old Python server
Remove-Item -Recurse -Force "$env:USERPROFILE\.unity-mcp" -ErrorAction SilentlyContinue   # older variant
```
Leave `$env:USERPROFILE\.unity\` alone — that's where the official relay lives.

### Stale bridge sockets
After closing Unity, clear leftover bridge handshake files:
```powershell
Remove-Item -Force "$env:USERPROFILE\.unity\mcp\connections\bridge-*.json" -ErrorAction SilentlyContinue
```

## 2. Install the official Unity AI Assistant package

In `Packages/manifest.json`, ensure this dependency exists:
```json
"com.unity.ai.assistant": "2.9.0-pre.2"
```
(or newer — it's on Unity's registry, no git URL needed). Open the project in Unity 6 and let it resolve.

The package ships the MCP bridge inside the Editor and writes the relay binary to:
```
%USERPROFILE%\.unity\relay\relay_win.exe
```
If that file isn't there after the package imports, open `Window → AI → Assistant` once — first-run provisions the relay.

## 3. Register the MCP server with Claude Code

Add this to `%USERPROFILE%\.claude.json` under the top-level `mcpServers` key (merge with whatever else is already in there — don't overwrite the file):

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "%USERPROFILE%\\.unity\\relay\\relay_win.exe",
      "args": ["--mcp"]
    }
  }
}
```

Or via CLI (equivalent):
```powershell
claude mcp add unity-mcp "$env:USERPROFILE\.unity\relay\relay_win.exe" --scope user -- --mcp
```

## 4. Verify

```powershell
claude mcp list
# Expect:
# unity-mcp: ...\.unity\relay\relay_win.exe --mcp - ✓ Connected
```
Unity must be open with this project loaded for the bridge to be available. From a Claude Code session, calling `mcp__unity-mcp__Unity_GetProjectData` should return the Assets taxonomy.

## Notes

- The bridge runs only while the Editor is open. Closing Unity shows the server as disconnected — that's normal.
- During domain reloads / script recompiles, MCP calls can hang briefly. Wait — don't retry.
- Occasional `Handshake failed: Connection closed during write` warnings from `Unity.AI.MCP.Editor.Bridge` are cosmetic — the named-pipe transport retries on its own.
