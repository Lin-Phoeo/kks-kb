# Vanity Studio: enable existing guards for one reviewed PseudoMaker build

The reviewed Vanity Studio Free package has compatibility prefixes for PseudoMaker, but its registration gate only accepts version 1.5.1.1. The installed 1.5.2.0 retains the same relevant UI refresh and accessory trimming behavior, so those safeguards are silently skipped even though the required fields and method signatures exist.

This patch extends only the version decision in each component's `WardrobePseudoMakerGuards.EnsureInstalled`. Original 1.5.1.1 behavior remains unchanged. For 1.5.2.0, the loaded assembly must have the exact reviewed name and SHA-256 before the existing signature checks and both original prefixes run. Unknown same-version binaries, wrong GUIDs and future versions remain rejected. The helper reads the file during first registration; the existing installed/unsupported guards prevent repeated checks after success or refusal. PseudoMaker itself is not modified.

The existing prefixes prevent a passive numeric-field refresh from clamping and writing an already out-of-range value, and protect explicitly reserved accessory tail slots during a coordinate change. They do not remove normal input bounds or disable trimming for unreserved slots. Their implementations remain unchanged.

| Artifact | Version | SHA-256 |
|---|---|---|
| Original Dress-Up component | 1.11.7 | `69b355bf1018ab22e55e4ddfd146421b97c66542ad89ed9037c94f8eaa0ee19b` |
| Local patched Dress-Up | 1.11.7.1 | `2dc5cb77fff929814e912d42b5a38de85ad9e5cfd99ebd1a1103967b9ef1e24f` |
| Original Make-Up component | 1.0.3 | `0c05fc9066d39c039358bd79283510efff400ae8ac5b3519398ba34504347d6a` |
| Local patched Make-Up | 1.0.3.1 | `6f4271ccb0d92b6745f1cdce91c415d87da224d5a81e758395a43d1538fdc222` |
| Reviewed PseudoMaker | 1.5.2.0 | `72e1c3a3dcb2120225f2d2823729cea81b2a2dffc8243de63ac6a4c901061882` |

The author's enclosing package is named release 1.6. Package version, component versions and local patch versions are distinct identities. Neither local patch is an author release. Each component changes only one existing method; 1,761 Dress-Up and 1,159 Make-Up methods remain unchanged. Runtime references are unchanged; the identity helper is embedded, not installed separately.

## Explicit local verification

Windows, PowerShell 7, .NET SDK 10, the existing .NET Framework 4 compiler and operator-provided Mono.Cecil 0.10.4.0 are required. No game files or dependencies are downloaded.

```powershell
./verify.ps1 `
  -DressUpPath '<reviewed package>/KKS_F604DressUp.dll' `
  -MakeUpPath '<reviewed package>/KKS_F604MakeUp.dll' `
  -PseudoMakerPath '<installation>/BepInEx/plugins/KKS_StudioPseudoMaker.dll' `
  -GameRoot '<installation>' `
  -CecilPath '<installation>/BepInEx/core/Mono.Cecil.dll' `
  -ArtifactsPath '<new external directory>'
```

The wrapper generates both exact candidates outside this source tree, extracts actual CIL into synthetic fixtures, checks original/candidate behavior in 16 runs, and verifies four refusal cases and preserved input bytes. It does not install plugins, run Unity, move mods or edit user content. Detailed evidence limits are in [VALIDATION.md](VALIDATION.md).

## Startup and data boundaries

This is a compatibility gate patch, not a guarantee that all Vanity functionality is safe or equivalent to PseudoMaker. Vanity's UI starts hidden, but it registers character extension handlers. Users explicitly invoking edits, Copy All or save operate on live character/card data. PseudoMaker remains installed; this patch does not replace it.

Make-Up's startup code calls pending cleanup only if a compatible cleanup backend can be created from its host. The exact reviewed Free host lacks `Freelancer604.Wardrobe.WardrobeCleanup`, so backend creation returns false and that startup call is unreachable for this pair. The wrapper verifies the real host metadata and runs the actual factory early-return CIL in a fixture. Different Free/Plus packages are not covered; do not generalize this result to arbitrary editions or skip checking for old pending tasks during deployment.

## Installation and rollback

Installation is separate. Stop Studio, recheck all three input identities, and back up both target DLLs/configuration if present. Install only the two reviewed local candidates together, verify both hashes and preserve a receipt. Do not copy helper, fixtures, reference assemblies or decompiled sources. Keep PseudoMaker unchanged.

Rollback while Studio is stopped requires the installed hashes to match the receipt. Restore prior DLLs, or move newly added DLLs into an external rollback directory when they were absent before installation. Preserve newly generated configuration and later user modifications. Character edits and saved cards are user data, not something a DLL rollback can reverse automatically; use independent test copies for feature acceptance.

## Reference startup observation

The exact two patched components were installed in one reference KKS environment with the host native language set to Chinese. Both loaded in an empty Studio scene and the process exited after normal window close. No character/card was loaded, no editing or saving was invoked, and PseudoMaker bytes were unchanged. This is startup evidence only, not full wardrobe/makeup or card-format acceptance.
