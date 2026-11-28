# Selected placement and character-filter verification

This change records two explicitly selected KKS Studio additions and a defect exposed while testing the placement workflow. It does not expand installation to the other research candidates.

## Aligned Placement Helper 1.0.1

Mixed selections can include Guides whose previous transform was not cached. The original command builders still enumerate every selected Guide, producing a KeyNotFoundException. The [targeted transformation](aligned-placement-selection-history/README.md) filters command entries by the actual cache and leaves the placement calculations intact. No runtime helper or update loop is introduced.

Actual CIL checks reproduce six failures in the original and none in the candidate across ten cases. A separate empty-Studio run passed 142 assertions using three temporary native Folders, the toolbar callback, position/rotation/circular placement, native Undo/Redo, mixed selection and cleanup. Some assertions repeat object-ownership checks; this is not 142 independent features. Random animation speed and complex character hierarchies remain unverified. Circular placement retains two native undo commands.

## Studio Chara Filter v1.4.1

The [official KKS release](https://github.com/WolfLikesKKS/KKS-Studio-Chara-Filter/releases/tag/v1.4.1) was used unchanged. Its release archive hash matched the published asset digest, and 235 non-framework member references resolved against the target dependencies. Sixteen isolated checks used the actual DLL with synthetic card metadata to verify parsing, name matching, malformed-file isolation and cache reuse.

The first temporary runtime observer executed its internal sequence twice and failed two count-restoration checks. That failed evidence was retained. Only the observer was revised: guarded single entry, lifecycle counters and a stable-list wait, with the same exact count assertion. A separate empty-Studio run then passed 28 checks with one start, one sequence and maximum concurrency one. The native character-list window laid out, an unmatched query returned no entries, clearing it restored the original count, and the menu, query, configuration and empty scene were restored. The official DLL did not change.

Initial configuration disables card removal and random-character buttons, with one indexing worker per sex. Local indexing is still performed. This does not verify all filter combinations, character replacement, replacement undo, removal or resource-report accuracy. Original card-file metadata was unchanged during the observed run; no private cards or logs are published here.

## Empty-selection callback discovered during cleanup

The placement run exposed an existing PPEv3 callback calling `First()` on an empty selection before its original exception handler. The [separate minimal repair](ppev3-empty-selection/README.md) returns for an empty list while preserving the previous focus and normal nonempty-selection behavior. Its verification and deployment status are recorded separately; passing the two new-plugin checks does not imply every existing plugin or shutdown log is healthy.

## Traceability and limits

Published material contains our transformation tools, synthetic collaborators and reviewed descriptions. Original third-party assemblies, game references, decompiled classes, private character metadata and complete runtime logs remain outside this repository. Tools require exact input hashes and produce new private outputs; they do not install plugins or launch the game. Temporary runtime observers are removed after validation.

Known console shutdown InvalidHandle/ThreadAbort messages are retained as unresolved. A successful normal process exit does not turn those logs into a zero-error result. The previous failed observer run is not retroactively relabeled successful.
