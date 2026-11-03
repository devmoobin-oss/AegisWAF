# AegisWAF Setup (Windows Installer)

Packages the self-contained AegisWAF Desktop build (admin console + embedded gateway) into a single
`AegisWAF-Setup-x64.msi` that can be copied to another Windows machine and installed with a normal double-click.

## Build it

On a Windows machine with the .NET SDK and the WiX CLI (`dotnet tool install -g wix` — also needs the
`WixToolset.UI.wixext`, `WixToolset.Util.wixext`, and `WixToolset.Firewall.wixext` extensions, added once with
`wix extension add <name>/5.0.2 --global`):

```powershell
installer\build-installer.ps1
```

This produces:
- `dist/AegisWAF-Setup-Build/` — the raw self-contained win-x64 publish output.
- `dist/installer/AegisWAF-Setup-x64.msi` — the installer to hand to the target machine.

Both folders are git-ignored (binary build output); only the installer *source* (`Product.wxs`, `License.rtf`,
`build-installer.ps1`) is checked in.

## Install it on another machine

1. Copy `AegisWAF-Setup-x64.msi` to the target Windows machine (win-x64; no .NET runtime needs to be preinstalled —
   the build is self-contained).
2. Double-click it and follow the wizard. It installs to `C:\Program Files\AegisWAF`, adds Start Menu and Desktop
   shortcuts, and adds a best-effort Windows Firewall allow rule for the app.
3. Make sure a PostgreSQL server is reachable from that machine (local install or remote). Nothing else needs to be
   prepared ahead of time.
4. Launch **AegisWAF** from the Start Menu. The first run walks through a 3-step setup wizard: database connection,
   the website to protect, and starting the gateway. There is no login step — the admin console is unauthenticated
   by design in this build, so only deploy it on machines/networks you control.

Settings are saved per Windows user under `%AppData%\AegisWAF\gateway.settings`, encrypted with DPAPI (useless if
copied to another machine or opened by another Windows account).

## Uninstall

Use *Settings → Apps → Installed apps* (or *Add/Remove Programs*) → AegisWAF → Uninstall. This removes the
installed files and shortcuts; it does not touch the PostgreSQL database.

## Silent install (for scripted rollout)

```cmd
msiexec /i AegisWAF-Setup-x64.msi /quiet /norestart
```
