# Coordinate Load Option explicit installation verifier

This is an opt-in, read-only installation acceptance tool. It is not part of the automated test suite. Run it only when the user has authorized inspection of an installation and explicitly supplied the game root, installed Coordinate Load Option DLL, and candidate adapter DLL paths. It does not discover an installation or use a default game path.

The game root supplies the local BepInEx and Unity reference assemblies. Build and intermediate output must be directed to an external evidence directory. Neither proprietary binaries nor local reports belong in the public source tree.

## Invocation

Requires the .NET 10 SDK for the verifier and .NET Framework 4.7.2 reference assemblies for the candidate build. Substitute authorized absolute local paths for the placeholders:

```powershell
$gameRoot = '<authorized game root>'
$installedPlugin = '<authorized Coordinate Load Option DLL>'
$evidenceRoot = '<external local evidence directory>'

dotnet build ops/studio-localization/sources/coordinate-native/StudioCoordinateNative.csproj -c Release "-p:GameRoot=$gameRoot" --artifacts-path "$evidenceRoot/build"
dotnet build ops/studio-localization/tools/verify-coordinate-native/CoordinateNativeVerifier.csproj -c Release "-p:GameRoot=$gameRoot" --artifacts-path "$evidenceRoot/build"

dotnet "$evidenceRoot/build/bin/CoordinateNativeVerifier/release/CoordinateNativeVerifier.dll" $gameRoot $installedPlugin "$evidenceRoot/build/bin/StudioCoordinateNative/release/Codex.StudioCoordinateNative.dll"
```

The JSON output reports the inspected binary hashes and the evidence limits. A failed assertion exits unsuccessfully. The tool never invokes Unity native code, opens Studio UI, loads a character or scene, changes configuration, or installs an adapter.

## What the 43 checks establish

| Checks | Evidence | Limit |
| --- | --- | --- |
| 8 | Actual installed DLL hash; native getter signature and embedded English resource via Mono.Cecil; exact set of resource identities; getter caller modules; English-culture literal and enum enumeration in the UI builder; unchanged source hash | Static metadata and IL observations. The enum check establishes the presence of enum enumeration, not a complete proof of UI identity semantics. |
| 24 | Every resource key extracted from the actual DLL resolves to a nonempty candidate Chinese value | A managed dictionary check using real keys. It does not establish translation quality or rendered labels. |
| 6 | Synthetic dictionary cases for unknown keys, explicit language, null keys, case sensitivity, ABMX preservation, and distinct mode labels | Managed boundary behavior only. |
| 3 | Reflection calls to the actual candidate prefix for known, unknown, and explicit-language requests | Candidate managed behavior, without installing a Harmony hook in Studio. |
| 2 | Candidate process filter and direct prefix call references via Mono.Cecil | Static metadata/IL checks, not a general side-effect proof. |

Review Chinese wording against the original resource and plugin behavior separately. The adapter covers the 24 known resource identities; literal UI strings outside that resource API, including the two `All` buttons, remain outside this hook. A successful report does not mean the whole plugin is translated, has zero translation overhead, or performs better in a running game.

Runtime acceptance still requires opening the coordinate-load UI in Studio, checking the selection labels and add/replace mode labels, and closing Studio normally.
