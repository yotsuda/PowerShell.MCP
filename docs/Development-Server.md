# Development server: testing proxy changes in the same conversation

A proxy change can normally only be tried in a **new** conversation: the MCP
client owns the running `PowerShell.MCP.Proxy.exe`, and Claude Code never
restarts a proxy it lost. The setup below registers a second, development
server, `pwsh-dev`, behind [mcp-sitter](https://github.com/yotsuda/mcp-sitter),
which can be killed, rebuilt and respawned from inside the conversation that
is working on it — while the production `pwsh` server keeps serving that same
conversation.

## How the two servers are kept apart

| | `pwsh` (production) | `pwsh-dev` (development) |
|---|---|---|
| Proxy exe | installed module's `bin\win-x64\` | `C:\MyProj\PowerShell.MCP.dev\PowerShell.MCP\bin\win-x64\` |
| Module the consoles import | `PowerShell.MCP` by name | the dev psd1 (`POWERSHELL_MCP_MODULE`) |
| Pipe names | `PSMCP.*` | `PSMCPDEV.*` (`POWERSHELL_MCP_PIPE_NAME`) |

- **Separate exe path.** `sitter_kill` kills *every* process running the child
  exe. Pointed at the installed proxy, it would kill the production server of
  this and every other conversation.
- **Separate pipe namespace.** A proxy claims any *unowned* console it finds —
  one a user started, or one released when its proxy died. `sitter_kill`
  releases the dev consoles, so without this the production proxy would claim
  them and run the dev DLL, and the dev proxy would claim leftover production
  consoles and run the old one. With different base names neither sees the
  other's pipes. A console a user starts by hand keeps `PSMCP` and stays with
  production.
- **Explicit module path.** Consoles do not inherit the proxy's environment
  (the Windows launcher builds them a fresh user environment block on purpose),
  so neither PSModulePath nor the pipe variable would reach them. The proxy
  hands both over in the init command it sends each console it starts:
  `$env:POWERSHELL_MCP_PIPE_NAME = 'PSMCPDEV'; Import-Module '<dev psd1>' -Force`.

Both variables are read only when set; unset, everything behaves as before.

## One-time setup

```powershell
npm install -g mcp-sitter

$devBase = 'C:\MyProj\PowerShell.MCP.dev\PowerShell.MCP'
New-Item -ItemType Directory -Force $devBase | Out-Null
Copy-Item (Join-Path (Get-Module PowerShell.MCP -ListAvailable)[0].ModuleBase 'en-US') $devBase -Recurse
.\Build-AllPlatforms.ps1 -Target Dll, WinX64 -OutputBase $devBase

# The native exe from mcp-sitter's platform package (not the npm .cmd/.ps1 shim)
$sitter = Get-ChildItem "$env:APPDATA\npm\node_modules\mcp-sitter" -Recurse -Filter mcp-sitter.exe |
    Select-Object -First 1 -ExpandProperty FullName

claude mcp add --scope user pwsh-dev `
    -e "POWERSHELL_MCP_MODULE=$devBase\PowerShell.MCP.psd1" `
    -e 'POWERSHELL_MCP_PIPE_NAME=PSMCPDEV' `
    -- $sitter "$devBase\bin\win-x64\PowerShell.MCP.Proxy.exe"
```

Start a new conversation once; its tools then include `mcp__pwsh-dev__*`.

## The loop

Run builds and git from the production `pwsh` server; exercise the change
through `pwsh-dev`.

1. Edit the proxy and/or module.
2. `mcp__pwsh-dev__sitter_kill` — frees the dev proxy exe.
3. `.\Build-AllPlatforms.ps1 -Target Dll, WinX64 -OutputBase C:\MyProj\PowerShell.MCP.dev\PowerShell.MCP`
   (a DLL still loaded by a dev console is swapped aside as `*.stash-*`).
4. For a module change, `mcp__pwsh-dev__close_console` the dev console so the
   next command starts one that loads the new DLL.
5. Call any `mcp__pwsh-dev__*` tool. The first response after the respawn
   starts with an `[mcp-sitter] server restarted …` notice that names the
   binary's build time and any tool-list change; check it to be sure the new
   build is the one answering.

If the dev server crashes, `mcp__pwsh-dev__sitter_child_stderr` shows the
proxy's stderr across restarts.

The installed module is untouched by this loop; deploy to it with a plain
`.\Build-AllPlatforms.ps1` once the change is ready (production sessions pick
the new proxy up in their next conversation).
