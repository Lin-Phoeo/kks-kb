# Preserve Scene Notes when importing a scene

SceneNotes 1.2.0 replaces the current note when another scene is imported. Importing a scene without a usable `notes` field also clears the current note. This local compatibility repair preserves the current note and appends a nonempty imported note with two line-feed characters between them.

The repair accepts only the reviewed KKS build of [ziglo's SceneNotes 1.2.0](https://www.patreon.com/ziglomods/posts/scenenotes-kk-162990091), plugin GUID `ziglo.scenenotes`. Obtain that original from the author. This directory contains patching source and synthetic fixtures; it does not redistribute the original, a patched binary, a full decompilation, user scenes, or game assemblies.

## Behavior and scope

For an import with current note `A` and incoming note `B`, the result is `A\n\nB`. Import without data, without the key, with a null dictionary, or with a null, empty, or non-string note leaves the current text unchanged. Import into an empty current note adopts the incoming text. An incoming note exactly equal to the entire current text is ignored. Whitespace-only notes remain text; existing Unicode and line endings are not normalized.

Normal scene loading still replaces the note, and clearing still clears it. Saving is unchanged: empty notes produce no plugin data; other notes use the author's existing version 1 data with a single `notes` string. No scene titles, provenance markers, additional data fields, or new assemblies are introduced. The author version remains 1.2.0; identify this local revision by its hash.

Only the original `SceneNotesSaveController.OnSceneLoad` method changes. Its three calls to `SetNotes` pass through one new private static helper that selects the import behavior. The other 29 original methods, including `OnSceneSave`, retain their instruction bodies. Field definitions and assembly references are unchanged.

## Reproduce verification

Use .NET SDK 10, PowerShell, the reviewed original DLL, and Mono.Cecil **0.10.4.0** from a compatible BepInEx installation. Supply a new artifacts directory outside this source directory. The command reads its inputs and writes only to that new directory; it does not install the result or modify game configuration.

```powershell
./ops/community-plugin-repairs/scenenotes-import/verify.ps1 `
  -SceneNotesPath '/path/to/author/ziglo_SceneNotes_KKS.dll' `
  -CecilPath '/path/to/BepInEx/core/Mono.Cecil.dll' `
  -ArtifactsPath '/path/to/new/private-verification-directory'
```

| Artifact | SHA-256 |
| --- | --- |
| Reviewed original | `23ead0fa3d7bc2ddeaa98163929b280f96f417f9d2aacde98cb0c09d0a44da8d` |
| Generated candidate | `536181e0a3c8ca34d62f12f493bad5282075c065b4580b929b746676ba52d6fa` |

The verification builds the patcher and fixture, generates the candidate, compares the method/field/reference scope, and extracts the actual original or candidate CIL into the fixture. The original runs 50 checks with 27 expected failures; the candidate runs the same 50 checks with zero failures. Four refusal cases check unknown patch input, identical input/output paths, an existing output, and unknown extraction input. Original and candidate hashes are checked again afterward.

The synthetic boundary supplies `GetExtendedData`, `SetExtendedData`, and the UI-facing `SetNotes` sink. The actual data lookup, import merge, and save instructions execute from the reviewed DLLs. Cases include absent and invalid data, empty notes, exact duplicates, whitespace, Unicode/CRLF, load/clear behavior, sequential imports, and save/load round trips. The scope check and refusal tests complement these behavioral checks; they do not replace them.

`verification-summary.json`, `scope.json`, `control.log`, and `candidate.log` record the results in the chosen artifacts directory. A separate independent run reproduced the candidate hash and the same outcomes. These are managed-code checks against synthetic boundaries. They do not prove that Unity displays the window correctly, that real scene import/save works end to end, or that the plugin is compatible with every other installed UI extension.

## Installation and rollback boundary

Installation is a separate, explicitly scoped operation. Close Studio normally, preserve the exact installed DLL and its hash before replacing anything, ensure only one copy of this plugin GUID is loaded, and copy only the verified candidate. Validate loading and import with self-created test scenes before relying on the change for existing work.

To roll back, close Studio and restore the preserved original DLL only after checking that the installed file is still the expected candidate. Keep scene files, notes, and configuration. The unchanged version 1 save representation remains readable by the original plugin, although its original import-overwrite behavior returns. Do not overwrite later user or updater changes during rollback.

## Reference runtime observation — 2026-11-28

The exact candidate was installed in a reference Studio environment. An empty-scene observer exercised the installed `SetNotes`, workspace-button colors, window state fields and import helper, then restored the prior text, visibility and button state. These checks belong to a combined 71-check UI observation with zero failures; they are not 71 SceneNotes-only tests. The observer did not dispatch real KKAPI scene-load/save callbacks or open user scenes, so real scene import/save and rendered text remain unverified. The temporary observer was removed after the process exited. See the [cross-component record](../selected-installation/README.md) for the evidence levels and remaining limits.
