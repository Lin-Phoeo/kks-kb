# Validation Record

Date: 2026-11-26

## Baseline identity

- Upstream: `IllusionMods/HSPlugins` commit `9220f2a6b2e6f8e0f62ea9acec6238f5ab6ed1cf`.
- Source file Git blob: `8eec8a31cc8a8235790f860aeacf7f565251332c`.
- Official release: r2.25, KKSPE assembly `2.21.5.0`.
- Official release KKSPE SHA-256 and installed KKSPE SHA-256 both: `af9a94a010b48681c84b6bb9ff4e11157f8cf228f5776a905cebc9b6031c8396`.

## Patch checks

- `git apply --check` against the exact source passed.
- Applying the patch produced a normalized source file byte-equivalent to the independently generated expected source.
- The source patch contains three hunks: one KKS-only duplicate-key registration fix and two unequal-array guards.
- The local Cecil candidate has three changed methods and 1,718 unchanged methods. Assembly types, fields, signatures, references, resources, and resource bytes were compared and remained identical.

## Regression harness

The harness transplants IL from the actual original and candidate assemblies into a small .NET fixture. It exercises 22 cases with a stub mesh, renderer, and editor.

| Build | Passed | Failed |
|---|---:|---:|
| Official KKSPE 2.21.5.0 | 12 | 10 |
| Candidate | 22 | 0 |

Covered cases include equal arrays; either side shorter; empty arrays; mesh shorter than unmapped values; editor busy; valid original high-index mappings to current mesh indices; missing original shape targets; initial registration; repeated registration for one owner; replacement owner for a reused key; and distinct keys.

## Not yet verified

- The original Unity scene and exact mismatched keyframe payload were not available as a runtime replay.
- The patched source has not been built with the full HSPlugins Unity dependency set.
- Studio animation, character replacement, and scene save/load have not yet been exercised with the candidate.
- No live plugin was replaced and no game process was started during this source preparation.
