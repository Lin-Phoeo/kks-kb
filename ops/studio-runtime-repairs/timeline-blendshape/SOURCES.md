# Sources

- Official HSPlugins r2.25 source: [BlendShapesEditor.cs at commit 9220f2a6b2e6f8e0f62ea9acec6238f5ab6ed1cf](https://github.com/IllusionMods/HSPlugins/blob/9220f2a6b2e6f8e0f62ea9acec6238f5ab6ed1cf/PoseEditor.Core/AMModules/BlendShapesEditor.cs).
- [PR #45](https://github.com/IllusionMods/HSPlugins/pull/45) fixed the same KKS duplicate-key exception by replacing `Dictionary.Add` with indexer assignment. The public patch restores that KKS-specific behavior.
- [Commit 61a9195](https://github.com/IllusionMods/HSPlugins/commit/61a9195cbe2c3b45f5e78d28e27519431620c716) changed the registration back to `Add` while consolidating BlendShapes implementations. The public patch adds the missing guards to group track interpolation.
- [Issue #130](https://github.com/IllusionMods/HSPlugins/issues/130) and [PR #135](https://github.com/IllusionMods/HSPlugins/pull/135) concern character-swap/save-load blendshape-track retention and XML/parenting behavior. Their fix is present in the current release but does not guard interpolation array lengths.
- [PR #221](https://github.com/IllusionMods/HSPlugins/pull/221) is closed without merge and addresses adding a track before selecting a mesh. It does not fix unequal left/right keyframe arrays.
- The public `GetBlendShapeName` and `SetBlendShapeWeight` paths are retained. The patch uses the existing name resolution when `_oriBlendIndex` is populated, preserving mapped original indices and skipping indices whose current name no longer resolves.

The project source includes its MIT license in `LICENSE`. The patch is an independently prepared small modification against that source; no upstream binary or complete source file is redistributed.
