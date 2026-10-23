# PCL2 clean Windows installer

Output: [`dist/PCL2_Setup.exe`](../../dist/PCL2_Setup.exe), a single Windows GUI executable for PCL **2.13.1.1**. The adjacent audit JSON is optional verification information; it is not needed to install.

Build and test from the repository root in Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\build_pcl_installer.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\test_pcl_installer.ps1
```

The build uses `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and Windows/.NET libraries only. Running setup requires .NET Framework 4.5 or later. No download, installer framework, Java installation, or adjacent payload archive is needed to run setup. Java requirements for launching Minecraft remain the launcher's responsibility. The setup binary is unsigned.

## Payload and privacy

Only these entries are embedded in `PCL2.Payload.zip`:

```text
Plain Craft Launcher 2.exe
PCL/
PCL/JavaWrapper.jar
PCL/libjlw-x86_64-1.4.4.dll
PCL/libwebp.dll
PCL/Setup.ini
PCL/Musics/
PCL/Pictures/
```

The four original binaries are copied byte for byte from the explicitly named source paths. `Setup.ini` is newly generated and empty, allowing the launcher to initialize its defaults. Both asset folders are empty. Source configuration, logs, launch scripts, registry exports, `.minecraft`, and asset contents are never copied. The builder accepts `-SourceExe` and `-RuntimeDirectory` overrides; it verifies the executable version.

The builder reads excluded launch/log files solely to collect personal token, identifier, and email values in memory. It scans the payload as UTF-8 and both UTF-16 byte orders/alignment offsets, the wrapper JAR's decompressed entries, the embedded archive extracted from the compiled installer, and the final executables. The known private username is also checked explicitly. Neither source log lines nor private values are printed or saved. The build fails before publishing if any value matches. `dist/PCL2_Setup.audit.json` records only counts, approved filenames, and SHA-256 hashes. This verifies the supplied personal values and allowlist; it is not a claim to identify every possible unknown secret format.

## Install and uninstall behavior

Setup defaults to `%LOCALAPPDATA%\Programs\PCL2`. It offers a folder picker, independently selectable Desktop and Start Menu shortcuts, launch after Finish, and progress reporting. All three checkboxes default to selected. Extraction runs on a background worker. Setup verifies each file's SHA-256 before registration, and rolls back its files and integrations if installation fails.

Setup requires an empty destination and rejects existing PCL2 registration. Uninstall an earlier installation before reinstalling, and use a fresh empty folder if preserved game files remain. Existing unrelated files and shortcuts are not overwritten. Local drive roots, network paths, and paths containing junctions or symbolic links are rejected.

The generated `Uninstall.exe` and `pcl2-install.state` are installer support files added at installation time; they are outside the five-file launcher payload. Registration uses only:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2
```

Shortcuts are named `Plain Craft Launcher 2.lnk` in the current user's Desktop and Start Menu Programs folder, with the installation folder as their working directory. The launcher runs only after a successful installation when Finish is clicked and the launch checkbox is selected.

Use Windows Installed Apps / Add or Remove Programs, or run `Uninstall.exe`. Uninstallation checks for locked files, removes the five packaged files, its own support files, matching selected shortcuts, its registration, and empty installation directories. Games and other added files are retained. A temporary copy of the uninstaller removes the installed executable after the original process exits; a hidden built-in PowerShell process removes that temporary copy after completion. No recursive deletion is used.

## Validation

The test harness compiles the production extraction/uninstall engine and GUI, using in-memory registration and shortcut selection doubles. It covers exact installed hashes and inventory, empty assets/configuration, progress, optional shortcuts, paths with spaces/apostrophes/Unicode, occupied folder protection, existing registration, shortcut/registration failure rollback, corrupt payload rejection, user file preservation, locked files, invalid ownership records, unsafe paths, and GUI defaults/layout. A separate test exercises the production Windows `.lnk` creation/removal functions with real shortcuts located entirely inside the workspace.

The GUI test produces `build/pcl2/installer-preview.png`. Tests do not write the user's real registry registration or Desktop/Start Menu, or launch the personal launcher. Full registration via Windows Settings, the temporary uninstaller handoff, and launching PCL2 should receive a final smoke test on a clean Windows account or VM before distribution. Build outputs are ignored by the repository's existing `dist/` rule.

The PowerShell test runner also contaminates a disposable runtime fixture with the known private username and verifies that the builder rejects it, emits no private value, and leaves the verified installer and audit unchanged. In total there are 13 compiled runtime tests and one privacy rejection check.
