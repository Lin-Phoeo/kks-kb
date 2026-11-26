# Compatibility guard reproduction

This directory contains source-only patch logic and a self-contained synthetic fixture. It does not include third-party DLLs, game files, logs, or generated outputs.

Run from this directory with .NET 10 SDK and your existing BepInEx Mono.Cecil reference. Choose a new output directory outside the repository:

```powershell
./run-regression.ps1 -CecilPath 'D:/YourGame/BepInEx/core/Mono.Cecil.dll' -OutputRoot 'D:/PatchChecks/run-001'
```

The script builds the patcher and fixture tester, keeping both final and intermediate output under `OutputRoot`. The tester generates patched fixture assemblies there, runs original and patched behavior in isolated load contexts, and writes `results/test-results.json`. The expected result is 19/19 checks with zero failures. These are synthetic method-shape tests; they do not prove real Unity Studio scene behavior.

The production patcher and tester compile the same `GuardPatches.cs`. Only the production entrypoint reads third-party input: it requires the exact original hashes in `SUPPORTED-ARTIFACTS.json` and refuses to overwrite inputs or existing outputs. The tester uses the generated fixture only.

## Dependency boundary

There are no NuGet package dependencies. `OtherRuntimeGuards.csproj` and `Tester.csproj` take `CecilPath` explicitly; no third-party DLL is distributed. The 2026-11-26 `dotnet list package --vulnerable --include-transitive` check reported no vulnerable NuGet packages for all three projects. This result does not audit your separately provided Mono.Cecil binary or Unity/BepInEx installation.

## Recorded check

On 2026-11-26 the three projects built with 0 warnings and 0 errors; the shared-algorithm fixture passed 19/19. The public patcher regenerated all three supported real inputs in an external scratch directory, with output hashes matching `SUPPORTED-ARTIFACTS.json`. No generated binaries or path-bearing receipts are included here. Real Unity Studio validation remains pending.
