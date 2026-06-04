# LanShare Agent Memory

## Project Overview

- Project name: `LanShare`
- Stack: `.NET 8`, `WPF`, `MVVM`
- Target OS: `Windows 10`, `Windows 11`, `Windows Server 2022`
- Architecture now uses two separate desktop apps:
  - `LanShare.Client`
  - `LanShare.Server`
- Original single-app project `LanShare.csproj` is still present for historical compatibility, but the active delivery model is dual-app.

## Current Product Direction

- This is a LAN file sharing tool.
- It does **not** use `SMB`.
- Discovery uses `UDP broadcast`.
- File operations use `HTTP` APIs over `TCP`.
- Main supported actions:
  - service discovery
  - browse shared directories
  - download files
  - upload files
  - delete entries
  - create folders
- Permissions are path-based, directory-level, and support inheritance to child directories.
- Config is JSON-based, no database.

## Current App Split

### Client

- Project: `LanShare.Client/LanShare.Client.csproj`
- Window title / tray text should be Chinese:
  - `局域网共享-客户端`
- Startup flow:
  - show built-in server address in address bar
  - address bar disabled initially
  - try built-in address first
  - if built-in connect fails, try UDP discovery
  - only if both fail, enable manual address input
- Discovery button logic:
  - enabled only when not transferring and not currently connected to the built-in server
- Client tray behavior:
  - minimize stays on taskbar
  - close hides to tray
  - tray supports restore and exit
- Current built-in server config is no longer hardcoded in code:
  - `Client.BuiltInServerHost`
  - `Client.BuiltInServerPort`
- Client should preserve real LAN IP in the address bar and must not rewrite it to `127.0.0.1`.

### Server

- Project: `LanShare.Server/LanShare.Server.csproj`
- Window title / tray text should be Chinese:
  - `局域网共享-服务端`
- Server tray behavior:
  - close hides to tray
  - tray supports restore and exit
- Server UI direction:
  - left side: service config + connected client list
  - right side: permission editor + shared path preview + permission rules list
- Shared path preview is meant to help choose permission paths visually based on the current shared root.

## Permissions System

- Users:
  - `admin`
  - `guest`
- Permissions:
  - `Read`
  - `Write`
  - `Delete`
- Scope:
  - directory-level
  - optional inheritance to child directories
- Config location:
  - `Permissions.Users`
  - `Permissions.Rules`
- Rule editing behavior:
  - adding a new rule appends
  - editing an existing loaded rule replaces only that one rule
  - fully identical duplicate rules should not be added

## Networking / Ports

- User requested high ports, not commonly reserved low/global service ports.
- Current defaults:
  - service port: `49443`
  - discovery port: `49450`
- Important: built-in server address must not hardcode port in code. It must follow config.

## Config Notes

- Root config file: `lanshare.json`
- Important client config fields:
  - `BuiltInServerHost`
  - `BuiltInServerPort`
  - `PreferredServerAddress`
  - `AutoConnectPreferredServerOnStartup`
  - `DownloadFolder`
- Important server config fields:
  - `ServerName`
  - `SharedFolderPath`
  - `ServicePort`
- Discovery config fields:
  - `DiscoveryPort`
  - `BroadcastIntervalSeconds`
  - `DiscoveryTimeoutSeconds`

## Upload / Download Behavior

- Large file transfer uses HTTP streaming.
- Progress near `100%` may still mean request finalization is in progress.
- Current upload support now includes:
  - file upload
  - whole folder recursive upload
  - drag-and-drop upload
- Duplicate upload handling:
  - client checks for same-name files in target remote directory
  - duplicate files are skipped after user confirmation
  - if all files are duplicates, upload is cancelled
  - server also rejects overwrite by returning conflict if same-name file/folder already exists

## Shared Path Preview / Permission UX

- User felt separate “scan shared folders” block was redundant.
- Current direction:
  - preview shared root directly in the permission editor area
  - selecting a file/folder fills the relative path field
  - should feel similar to the client file list / file panel

## Visual / UX Preferences

- User dislikes flashy or overly decorative UI.
- UI should feel practical, plain, and professional.
- Prefer Windows-like layout and behavior.
- Client and server names should be Chinese-facing for customers.
- Consistency matters more than fancy visuals.

## Packaging Status

- Dual self-contained publish flow is ready.
- Publish script:
  - `scripts/publish-win-x64.ps1`
- Current publish outputs:
  - `publish/client-win-x64`
  - `publish/server-win-x64`
- Self-contained zip artifacts already produced:
  - `dist/LanShare-Client-self-contained-win-x64-0.01.zip`
  - `dist/LanShare-Server-self-contained-win-x64-0.01.zip`
- Old installer artifact exists:
  - `dist/LanShare-Setup-0.01.exe`
  - this belongs to the older single-app packaging path
- Dual installer scripts now exist:
  - `installer/LanShare.Client.iss`
  - `installer/LanShare.Server.iss`
- Installer build script:
  - `scripts/build-installer.ps1`
- Current blocker for producing fresh dual `Setup.exe` installers:
  - `ISCC.exe` was not found in standard Inno Setup install paths during recent checks

## Build / Validation Notes

- When normal `dotnet build` fails because client/server EXEs or DLLs are locked by running processes, temporary output builds were used successfully:
  - client verify output under `_verify/client*`
  - server verify output under `_verify/server*`
- This means source-level validation was successful even when active processes locked standard build output paths.

## Important Historical User Requests

- Client/server must be separate applications.
- Client should be extremely simple for end users.
- Built-in server address should be preferred first.
- Manual address entry should be hidden/disabled unless automatic methods fail.
- Customer environment may use A/B/C private ranges, not only C segment.
- Server side is generally one machine; client side can be many machines.
- Customer may preinstall client into system images.

## Practical Repo Notes

- There are many generated directories in this workspace:
  - `bin`
  - `obj`
  - `publish`
  - `dist`
  - `_verify`
  - `artifacts`
- These should not be committed except when a human explicitly wants release artifacts tracked outside git.

## If Another Agent Continues

- Prefer working in the dual-app projects, not the old single-app shell.
- Be careful with Chinese text encoding; some files previously contained mojibake and were rewritten manually.
- Before claiming a packaging result, distinguish clearly between:
  - self-contained publish folders / zip packages
  - actual installer `Setup.exe` packages
- Before running a standard build, check whether client/server processes are still running and locking outputs.
