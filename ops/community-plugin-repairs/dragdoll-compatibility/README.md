# DragDoll: distinguish the known basic Extra Move Axis build

DragDoll KKS 1.0.37 finds a plugin by the type name `StudioExtraMoveAxis.StudioExtraMoveAxisPlugin` and then requires the enhanced input contract: middle-click reset, drag fields and a `ShowGizmo` property. The supported basic Extra Move Axis 2.0 uses that type name but does not implement the enhanced contract. Startup consequently logs `Extra Move Axis FK preparation stopped` / `Extra Move Axis input contract changed` before any bridge patch is installed.

This source-only patch adds one exact identity decision to `ExtraMoveAxisInputBridge.Tick`. Only the reviewed basic build is classified as not applicable to the enhanced bridge. It emits a single informational decision and stops retrying that optional bridge. DragDoll's original native guide hooks, other compatibility bridges, input callbacks and pose behavior are unchanged. Unknown same-name builds still receive the original contract checks and errors. A complete enhanced contract still installs both direct patches and invokes all four original secondary installers.

| Input or output | SHA-256 |
|---|---|
| Supported original DragDoll KKS 1.0.37 | `5938a51b4db8ede31e459bfe8bc8e0735bcf10957727a230af39ecf69a1e9f1b` |
| Known basic Extra Move Axis 2.0 | `c75421f9f12e23b797883469b313c794568db8c2242c44e30a1541360170cc74` |
| Reviewed patched DragDoll | `72a6c00dfe75a84d046fd7bcbfde50798b85e1f7e6b147666415666587bf8a3a` |

The patcher requires both exact input hashes. The embedded helper checks the loaded Axis type name, assembly name and actual file SHA again at runtime. It reads the Axis file once when classifying the supported build; the existing `unavailable` guard prevents repeated classification. File access failures remain observable through the original bridge error path. The output changes one original method and adds only self-contained identity helpers; 1,459 original methods and the assembly reference list remain unchanged. No separate helper DLL is installed.

The archived official Extra Move Axis v3.1 is a different binary with a 3.1 plugin identity. It implements additional reset/drag fields but lacks the complete `ShowGizmo` contract. This patch does not classify it as the known basic 2.0 or pretend an upgrade to it supplies all enhanced functionality.

## Explicit local verification

This tool is opt-in and reads only operator-supplied plugin and game metadata references. Requirements: Windows, PowerShell 7, .NET SDK 10, .NET Framework 4 compiler, and existing Mono.Cecil 0.10.4.0. It does not download or install game dependencies.

```powershell
./verify.ps1 `
  -DragDollPath '<installation>/BepInEx/plugins/KKS_DragDoll.dll' `
  -BaseAxisPath '<installation>/BepInEx/plugins/KKS_StudioExtraMoveAxis.dll' `
  -GameRoot '<installation>' `
  -CecilPath '<installation>/BepInEx/core/Mono.Cecil.dll' `
  -ArtifactsPath '<new directory outside this source tree>'
```

All build products, local candidates, extracted CIL and reports stay in the supplied external directory. Unknown input hashes, existing output and input/output reuse are refused. The original DLLs are never written.

The existing and patched `Tick` CIL run against the same synthetic fixture in five scenarios each: known basic build, complete enhanced contract, unknown incomplete build, missing required field, and absent optional plugin. Assembly discovery and disk-hash inputs are the two explicit substitutions; the imported helper's identity comparison and the rest of Tick execute. Harmony and secondary installers are recorders, so this proves branch/registration preservation, not real Unity callbacks or full enhanced-plugin compatibility. See [VALIDATION.md](VALIDATION.md).

## Installation and rollback boundary

Installation is separate from generation. With Studio stopped, verify the original DragDoll and Axis hashes, back up DragDoll to a new external directory, copy only the reviewed patched DragDoll to the same plugin path, then verify its hash. Keep Axis unchanged and record a receipt. Do not copy helper build files, fixture assemblies or reference DLLs into plugins.

To roll back, stop Studio, require the installed DLL to still match the receipt, and restore the exact original `5938a51b...` backup. Preserve later modifications rather than blindly overwriting them. No card, scene or configuration change is part of this patch. A successful empty-scene startup can verify bridge classification and normal exit only; IK/FK posing, Timeline, VNGE and active guide interactions require their own acceptance.
