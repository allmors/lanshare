# LanShare Packaging Guide

## 1. Build the publish folder

Run from the project root:

```powershell
.\scripts\publish-win-x64.ps1
```

Optional `ReadyToRun` publish:

```powershell
.\scripts\publish-win-x64.ps1 -ReadyToRun
```

Output folder:

```text
publish\win-x64
```

This produces a self-contained `win-x64` build for Windows 10, Windows 11, and Windows Server 2022.

## 2. Build the installer

Install `Inno Setup 6`, then run:

```powershell
.\scripts\build-installer.ps1
```

Or open this script manually:

```text
installer\LanShare.iss
```

Installer output folder:

```text
dist
```

## 3. Packaging strategy

- Self-contained publish: target machine does not need a separate .NET 8 runtime install
- Non-single-file publish: safer for this app because it includes config files and network services
- ReadyToRun: optional cold-start optimization
- Install folder: `C:\Program Files\LanShare`
- User config folder: `%AppData%\LanShare\lanshare.json`

## 4. Config behavior

- `lanshare.json` is published with the app as the default template
- On first launch, the app copies that template to `%AppData%\LanShare\lanshare.json`
- After that, the app reads and writes the user-scoped config file instead of writing into `Program Files`

## 5. Before release

- Test on Windows 10, Windows 11, and Windows Server 2022 Desktop Experience
- Confirm Windows Firewall access on first launch
- Confirm the service port and UDP discovery port are available
- For public distribution, add code signing to reduce SmartScreen and antivirus warnings

## 6. Transfer architecture

- Server discovery uses `UDP broadcast`
- File browsing, upload, download, delete, and folder creation use `HTTP` APIs
- Actual file transfer runs over `HTTP` on top of `TCP`
- The project does not use `SMB`

## 7. Why file transfer uses HTTP/TCP

- `UDP` is used only for LAN discovery because broadcast discovery is simple and efficient
- `UDP` is not used for file payload transfer because reliability, retransmission, ordering, and flow control would all need to be implemented manually
- `TCP` is the better fit for file transfer because it already provides reliable ordered delivery
- `HTTP` keeps the implementation simple, debuggable, and easy to extend with permissions, browsing, upload, download, and deletion endpoints

## 8. Large file progress behavior

- Progress close to `100%` means the client has nearly finished sending or receiving the file stream
- Final completion only happens after the server finishes the write, closes the stream, and returns a successful response
- Because of that, large files may briefly show a finishing stage before the transfer is marked complete
- The UI now keeps the transfer in a finishing state near completion and only shows `Transfer completed` after the request fully succeeds

## 9. Current design choice

- Keep the current `UDP discovery + HTTP/TCP transfer` design
- This is the best balance of stability, implementation cost, maintainability, and LAN performance for the current LanShare scope
