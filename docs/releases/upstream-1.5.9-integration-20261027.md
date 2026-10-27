# Official DeskBox 1.5.9 integration

Integrates official 1.5.9 (`4cae1d4ebb0e416af7d66a764f52f6ccaafdda3a`) and the subsequent release-notes correction on upstream `main` (`1fdf0bd9f84affe889318173a7f3141843c79604`). Application and installer versions follow official 1.5.9, including the published release-notes history. Two existing runtime fixes remain necessary; no new fork features or version-specific update adaptations are introduced.

## Upstream replacement audit

| Existing fork change | Official 1.5.9 assessment | Integration decision |
| --- | --- | --- |
| Logon tray menu host | Official `App.Tray.cs` still selects H.NotifyIcon `SecondWindow` and constructs its host through `ContextFlyout`. Its retry/native-menu recovery already existed in the previously merged upstream baseline. The dependency remains `H.NotifyIcon.WinUI` 2.5.0-beta.1. The new Hidden style controls notification-icon visibility, not the shell-dependent menu host. | Retain the existing owned HWND host and tool-window styles, together with upstream retry, callback isolation, native fallback, Hidden style and menu appearance. |
| Persisted shortcut icons and transient failures | Official `ResolveIconSource` still starts from `ShortcutHelper.ReadStoredMetadata`; explicit indexed extraction still falls back through the resource file's Shell icon. The release adds namespace/Recycle Bin icon support and refresh, but no persisted `.lnk` icon reader, extensionless ICO decoder or transient-source cache exclusion. | Retain the existing persisted resource recovery, signed index/environment-block support, extensionless ICO decoding, retryable failures and bounded fourth hydration retry. Keep official namespace icon extraction and refresh. |
| Windows UNC Rust build paths | Official scripts still use `Resolve-Path(...).Path`, which can include the PowerShell provider prefix on this checkout. | Retain the existing `.ProviderPath` correction in the two Rust build scripts. |
| Feature behavior and dependency versions | Official 1.5.9 supplies the new context menus, selected-name expansion, recovery offer, update experience, Korean localization, display/capsule fixes and image-cache improvements. | Adopt upstream implementations and lock files. Do not add parallel fork implementations. |
| Commit identity and development workflow | Repository-specific author/committer identity, hooks, canonical Debug restart and framework-dependent installer workflow remain local delivery requirements. | Retain repository workflow and its existing verification support. |

The tray assessment is a source/dependency comparison. It does not establish whether the original early-logon failure can still be reproduced on a new Windows logon. The icon distinction is covered by the existing incomplete-metadata, missing-resource and extensionless-icon regression fixtures.

Application project version fields, package manifest, both installer definitions and the direct-release consistency script now match upstream exactly. The obsolete four-component fork-version adaptation is removed. Existing historical fork release notes remain intact.

## Validation

- Locked-mode restore succeeded with the pinned SDK 10.0.402. The cached official SDK archive's SHA-512 matches Microsoft's release metadata.
- Final x64 suite: **5,819 passed, 1 failed, 0 skipped, 5,820 executed**. The suite completed without aborting; it is not fully green. The application DLL for the x64 test build was written after that build started.
- The sole failing test is `AotPublishContractTests.AotAudit_RejectsArm64BeforeResolvingToolsOrTouchingArtifacts`. An independent invocation confirms that antivirus blocks the official `publish-aot-audit.ps1` at line 1 with `ScriptContainedMaliciousContent`, before the architecture guard can run. No antivirus exception, bypass or test weakening was applied.
- Direct-release consistency passes at official version **1.5.9**. The official current-version/release-history contract passes without modifying its implementation or test.
- The canonical non-platform Debug build succeeds with 0 errors and 20 warnings. `DeskBox.dll` was freshly written at `2026-10-27T15:31:48.5166044Z`, after the build started at `2026-10-27T15:30:28.6897180Z`; its file/product versions are `1.5.9.0` / `1.5.9`.
- The installed instance was stopped by its exact executable path before starting the canonical repository build. Exactly one DeskBox instance is running from `src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.exe`. The current launch completed **41 startup steps, 0 degraded, 0 failed**.
- All **9 widget identities and 1 group identity** survive the restart. Settings migrated from schema 14 to 15; changed keys are the new official update preferences (`autoDownloadUpdates`, `showReleaseNotesAfterUpdate`, `showUpdateToastNotification`), `schemaVersion`, `lastRunVersion` and `lastUpdateCheckAt`.
- Validation covers builds, tests, process identity, startup logs and persisted identities. A real Windows reboot/logon and physical interaction with the new menus have not been exercised. No installer was built or installed in this integration.
