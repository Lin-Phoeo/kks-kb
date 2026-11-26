# Unity.Console borrowed-console ownership patch

**Runtime acceptance failed; candidate rolled back.** The isolated ownership tests below pass, but the real game exit paused and ended in a native UnityPlayer crash. The previously observed invalid console handles were absent. That difference does not establish the crash cause. Keep this candidate out of normal installations until the shutdown interaction has been reproduced and resolved; source is retained for investigation.

This source-only tool prevents `Unity.Console.Program.Close` from releasing a native console that was already present when `Program.Run` started. A BepInEx console must remain available for other plugins' exit logging. The tool keeps the console and VNGE enabled.

The original `Run` allocates a console only when no console exists, but `Close` releases any existing console. If BepInEx created it first, that release invalidates the separately cached `CONOUT$` handles used by BepInEx's console listener and Unity.Console. Restoring `Console.Out` alone cannot repair an independent console listener writer.

The patch stores the existing `AllocConsole()` result in one private static bool. `Close` keeps the original REPL abort, then frees the console only if that bool is true. Only `Run` and `Close` change; there are no new runtime references, logging filters or exception suppressors. The output retains CLR v2.0.50727. The tool itself runs under .NET 10; that SDK is not a game dependency.

| Identity | SHA-256 |
| --- | --- |
| Sole supported original `Unity.Console.dll` | `1be20737d73ae8d23db5c08d3a86e992bc7b12b171401686ecb264e05b439cf6` |
| Reviewed candidate | `91874b52329d4fa4bb0b69a21e073d69ed423eacdb3485ee03ba985b0a7cefd0` |

Unknown inputs, an already patched input, an existing output and an existing receipt are rejected. Generation checks the reviewed candidate hash before writing. It does not overwrite its input or install anything. The required input/output hashes are intentional public compatibility identifiers; machine paths, user logs and game DLLs are excluded.

## Build and verify

Requirements: Windows, PowerShell 7, .NET SDK 10, your own supported input DLL, and your existing `Mono.Cecil.dll`. The verified build used Mono.Cecil 0.10.4.0 from the local BepInEx installation. No dependency is downloaded by this project. Other Cecil versions may produce a different output and will be rejected before writing.

Run from this source directory, replacing the example paths. The artifacts directory must not exist and must be outside this public source directory:

```powershell
./verify.ps1 `
  -InputPath '<installation>/BepInEx/plugins/Console/Unity.Console.dll' `
  -CecilPath '<installation>/BepInEx/core/Mono.Cecil.dll' `
  -ArtifactsPath '<new-external-directory>'
```

The script builds the tool and synthetic probe, generates the candidate and receipt, verifies the output hash, runs five native cases and five rejection cases, and checks the original is unchanged. Build products, candidate, local test output and `verification-summary.json` remain in the external artifacts directory. An existing artifacts directory is refused.

To generate only a candidate after building the tool:

```powershell
dotnet '<artifacts>/tool/ConsoleOwnershipPatch.dll' '<supported-original.dll>' '<new-candidate.dll>'
```

Never generate directly into an installation. Stop the game, back up and hash the original, inspect the candidate receipt, then make the separate installation decision. Rollback restores the exact original backup while the game is stopped.

## Test meaning and limits

The test code is original synthetic tooling. At execution time, `ProbeExtractor` reads the user-supplied original and candidate and maps their actual `Close` CIL into a synthetic fixture. It substitutes only the `UC.UnityConsole.Abort` target with a counting stub. `GetConsoleWindow`, `FreeConsole`, `CONOUT$`, `WriteFile` and `StreamWriter` are real Win32/.NET operations in independent hidden child consoles. These tests do not load or execute game, Unity or IronPython assemblies. Extracted fixture binaries are local artifacts and are not distributed here.

| Case | Expected result |
| --- | --- |
| Original Close | Both independently opened output handles fail with Win32 error 6; StreamWriter throws IOException |
| Candidate borrowing a console | Both handles and writer remain writable; two Close calls make no FreeConsole call; Abort remains called |
| Borrowed console with null REPL object | Handles remain writable; the null branch still reaches the ownership guard |
| Candidate owning a console | FreeConsole occurs once; second Close does not release it again |
| Owned console with null REPL object | Owned console is still released once |

Extracted allocation-result consumer instructions are tested with success/failure bool stubs. This verifies the changed store instead of the original pop; it does not execute full `Run` or actual AllocConsole inside the game. Rejection tests cover unknown input, already patched input, existing output, existing receipt and input/output identity, with byte preservation assertions.

Isolated results are in [evidence.md](evidence.md). Real startup and VNGE initialization were observed, but game exit failed acceptance as described above. Further acceptance requires normal VNGE/REPL operation and game exit, with exit logs checked for invalid handles, stream failures, recursion, crashes and hangs. Startup success alone is insufficient. This patch does not repair old stdout self-reference, hidden-start failures, cached IronPython writers after an intentionally owned console release, console reopen/toggle behavior or every exit race.

## Sources and rights

- [TheHologram/unity-console Program](https://github.com/TheHologram/unity-console/blob/a62851ff28bca7f084ff12228843481981c39554/Unity.Console/Program.cs), blob `a747717b6c6efe1b4dfef27ea24d1eda28b7b822`: original ownership behavior. Latest release [1.0.1](https://github.com/TheHologram/unity-console/releases/tag/1.0.1) is from 2017. Current source retains direct FreeConsole. The repository [LICENSE](https://github.com/TheHologram/unity-console/blob/master/LICENSE) describes mixed licenses and Apache 2.0 for original unmarked files. No upstream source or binary is redistributed here.
- [BepInEx v5.4.23.6](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6): console listener caches a native console writer. Current installed core already matched this version; reinstalling it would preserve the separate Unity.Console defect. BepInEx's licensing remains with its upstream repository; none of its source/binaries is included.
- [Magicgazer/KK_VngeQuitFix v1.1.1](https://github.com/Magicgazer/KK_VngeQuitFix/releases/tag/v1.1.1), MIT, source blob `65bd714bbf74b2673ae3e57612de703ec3451b66`: examined as a possible existing solution. It targets a KK CharaStudio DivideByZero shutdown hang, injects exit keys, waits for the original REPL exit path and may call FreeConsole itself. It does not enforce console ownership, and its KKS applicability to this fault is unverified. No code from it is copied here.
- [Mono.Cecil](https://github.com/jbevain/cecil), MIT: supplied by the operator at build time; not vendored or downloaded.

The patcher, extraction mapper, probe and verification script are newly authored project tooling. This directory does not grant a new independent license beyond the containing project's terms. Existing third-party license obligations are unaffected. No full decompiled class, private log, game assembly, screenshot, account identifier or installation path is included.
