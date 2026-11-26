# KKPE KKS Timeline BlendShape Guard Patch

This patch addresses two verified KKS `BlendShapesEditor` failure paths in the official HSPlugins r2.25 source:

- Group and Group Link interpolation reads both keyframes but iterates only the left array length. The patch limits interpolation to the shared array range. With original-index mapping active, it preserves valid original indices and skips entries whose current mesh no longer has a corresponding shape.
- KKS reuses `FaceBlendShape` across character replacement. `Init` registers that object in a dictionary with `Add`, which throws when the key is already present. The KKS-only registration changes to indexer assignment, matching the earlier upstream fix in PR #45.

The patch is source-only. It does not contain or distribute KKSPE.dll, Timeline.dll, game files, logs, or user scenes.

## Apply

The patch targets the exact source blob `8eec8a31cc8a8235790f860aeacf7f565251332c` at upstream commit `9220f2a6b2e6f8e0f62ea9acec6238f5ab6ed1cf`. Use a clean isolated HSPlugins checkout at that commit. Apply it from that repository root with:

```sh
git apply --check blendshape-runtime-guards.patch
git apply blendshape-runtime-guards.patch
```

Build only the KKS PoseEditor target using that repository's normal build instructions. Confirm your build output is from the patched source before replacing any installed plugin. Preserve and back up the current DLL first.

The patch does not update Timeline, ShalltyUtils, The Bird of Hermes, or any other component. This is a small backport to the existing official source, not an endorsement of replacing the installed plugin suite with a fork.

## Validation

An isolated harness extracted the three changed method bodies from the original and candidate KKSPE assemblies and ran them against synthetic editor/renderer stubs. The original passed 12/22 cases and failed 10; the candidate passed 22/22, including unequal/empty arrays, shorter meshes, busy state, valid old high-index mappings, missing shape mappings, and duplicate character registration. This is not a Unity scene replay. Actual scene playback, character replacement in Studio, and save/load remain required.

The candidate assembly changes only `BlendShapesEditor.Init`, group interpolation, and linked-group interpolation. It retains all 200 types, 1,256 fields, 19 assembly references, 5 embedded resources, and all 1,718 other method bodies. See `evidence.json` for hashes and test boundaries.

## Compatibility

This targets official HSPlugins commit `9220f2a6b2e6f8e0f62ea9acec6238f5ab6ed1cf` and KKSPE 2.21.5.0. It is not a general binary patcher and must not be applied to a different source revision or binary version without review.
