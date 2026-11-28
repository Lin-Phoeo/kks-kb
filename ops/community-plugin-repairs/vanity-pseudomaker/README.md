# Vanity Studio: enable existing guards for one reviewed PseudoMaker build

The reviewed Vanity Studio Free 1.6 and 1.7 packages have compatibility prefixes for PseudoMaker, but their registration gate only accepts version 1.5.1.1. The reviewed 1.5.2.0 retains the same relevant UI refresh and accessory trimming behavior, so those safeguards are silently skipped even though the required fields and method signatures exist.

This patch extends only the version decision in each component's `WardrobePseudoMakerGuards.EnsureInstalled`. Original 1.5.1.1 behavior remains unchanged. For 1.5.2.0, the loaded assembly must have the exact reviewed name and SHA-256 before the existing signature checks and both original prefixes run. Unknown same-version binaries, wrong GUIDs and future versions remain rejected. The helper reads the file during first registration; the existing installed/unsupported guards prevent repeated checks after success or refusal. PseudoMaker itself is not modified.

The existing prefixes prevent a passive numeric-field refresh from clamping and writing an already out-of-range value, and protect explicitly reserved accessory tail slots during a coordinate change. They do not remove normal input bounds or disable trimming for unreserved slots. Their implementations remain unchanged.

| Artifact | Version | SHA-256 |
|---|---|---|
| Original Dress-Up from Free 1.6 | 1.11.7 | `69b355bf1018ab22e55e4ddfd146421b97c66542ad89ed9037c94f8eaa0ee19b` |
| Local patched Dress-Up from Free 1.6 | 1.11.7.1 | `2dc5cb77fff929814e912d42b5a38de85ad9e5cfd99ebd1a1103967b9ef1e24f` |
| Original Dress-Up from Free 1.7 | 1.11.9 | `23f28a4528211bbcdbcb47ad0e2696e4299ae19353747e7b15668bd73313b0c2` |
| Local patched Dress-Up from Free 1.7 | 1.11.9.1 | `6961ec6cc0dcc52b0f593dde2f4034056f6734c3ad40420c721b37b7f467b91e` |
| Original Make-Up component | 1.0.3 | `0c05fc9066d39c039358bd79283510efff400ae8ac5b3519398ba34504347d6a` |
| Local patched Make-Up | 1.0.3.1 | `6f4271ccb0d92b6745f1cdce91c415d87da224d5a81e758395a43d1538fdc222` |
| Reviewed PseudoMaker | 1.5.2.0 | `72e1c3a3dcb2120225f2d2823729cea81b2a2dffc8243de63ac6a4c901061882` |

Package versions, component versions and local patch versions are distinct identities. Neither local patch is an author release. Free 1.7 contains the same Make-Up bytes as Free 1.6, so both produce the same patched Make-Up. Each component changes only one existing method; 1,761 Dress-Up and 1,159 Make-Up methods remain unchanged. Runtime references are unchanged; the identity helper is embedded, not installed separately. The Free 1.7 author's color-path and color-reset changes remain intact.

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

The wrapper recognizes either exact Dress-Up input above, generates its matching candidate and the common Make-Up candidate outside this source tree, extracts actual CIL into synthetic fixtures, checks original/candidate behavior in 20 runs, and verifies four refusal cases and preserved input bytes. Run it separately with each supported original to verify both outputs. It does not install plugins, run Unity, move mods or edit user content. Detailed evidence limits are in [VALIDATION.md](VALIDATION.md).

## Startup and data boundaries

This is a compatibility gate patch, not a guarantee that all Vanity functionality is safe or equivalent to PseudoMaker. Vanity's UI starts hidden, but it registers character extension handlers. Users explicitly invoking edits, Copy All or save operate on live character/card data. PseudoMaker remains installed; this patch does not replace it.

Make-Up's startup code calls pending cleanup only if a compatible cleanup backend can be created from its host. Both exact reviewed Free hosts lack `Freelancer604.Wardrobe.WardrobeCleanup`, so backend creation returns false and that startup call is unreachable for these pairs. The wrapper verifies the real host metadata and runs the actual factory early-return CIL in a fixture. Other Free/Plus packages are not covered; do not generalize this result to arbitrary editions or skip checking for old pending tasks during deployment.

## Installation and rollback

Installation is separate. Stop Studio, recheck all three input identities, and back up both target DLLs/configuration if present. Ensure the two reviewed local candidates are paired, verify both hashes and preserve a receipt. When upgrading a previously patched Free 1.6 pair to Free 1.7, Make-Up already has the same candidate hash and can stay in place; do not replace it with the unpatched author copy. Do not copy helper, fixtures, reference assemblies or decompiled sources. Keep PseudoMaker unchanged.

Rollback while Studio is stopped requires the installed hashes to match the receipt. Restore prior DLLs, or move newly added DLLs into an external rollback directory when they were absent before installation. Preserve newly generated configuration and later user modifications. Character edits and saved cards are user data, not something a DLL rollback can reverse automatically; use independent test copies for feature acceptance.

## Reference startup observation

The exact Free 1.6 patched components were installed in one reference KKS environment with the host native language set to Chinese. Both loaded in an empty Studio scene and the process exited after normal window close. No character/card was loaded, no editing or saving was invoked, and PseudoMaker bytes were unchanged. This historical startup evidence does not establish Free 1.7 startup or full wardrobe/makeup or card-format acceptance.

A subsequent separate empty-Studio observation of the exact Free 1.7 local pair passed 89 bounded checks: native prefix registration, Make-Up bridge attachment, empty-target shell lifecycle and state/configuration restoration. Drawing was suppressed to prevent reference-card access, so actual panel appearance, character editing and saved-data roundtrips remain unverified. See [the native observation boundary](VALIDATION.md#separate-bounded-native-observation).
