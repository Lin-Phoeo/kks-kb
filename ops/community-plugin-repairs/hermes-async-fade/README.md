# Hermes: preserve fades until asynchronous audio decoding completes

Hermes 5.7.1 restores a saved track's fade settings before its asynchronous decoder has produced audio. `ClampFade` immediately compares them against a zero duration and clears them. The decode completion branch updates audio and waveform data without clamping against the now-known duration. This also leaves oversized restored trim values unchecked until a later operation.

This exact-input local patch changes two existing methods. `ClampFade` returns while `HasAudio` is false, matching the existing `ClampTrim` boundary. On successful decode, `PollAsyncCompletion` calls the original `ClampTrim` and then `ClampFade` after setting waveform data. Filtering, explicit clearing, already-decoded clamping and the scene data format retain their original behavior. The remaining 11,011 methods and all assembly references stay unchanged; no helper DLL is installed.

| Artifact | Plugin version | SHA-256 |
|---|---|---|
| Author original | 5.7.1 | `d044e20742b7cfdf6dcf9ecf1c10c66a7376dfe263d42c50a33cb8cdbece3743` |
| Local compatibility candidate | 5.7.1.1 | `4a038039a29d930bd34799cc8c177f76817526bbeba1123f2af833e92aa3b9ec` |

The candidate is a local revision, not an upstream release. Its assembly version remains 5.7.1.0.

## Reproduce the candidate and regression evidence

Windows, Python 3.12+, .NET 10 SDK, operator-supplied Mono.Cecil and KKS reference assemblies are required. Run from the repository root with explicit paths:

```powershell
py -B ops/community-plugin-repairs/hermes-async-fade/run.py `
  --original 'E:/ReviewInputs/TheBirdOfHermes.KKS.dll' `
  --game-root 'E:/Games/Koikatsu Sunshine' `
  --cecil 'E:/ReviewTools/Mono.Cecil.dll' `
  --output 'E:/BuildArtifacts/hermes-fade-review'
```

The output must be new, outside both the repository and the game tree, and must not pass through a symlink or junction. Unknown originals are rejected. The wrapper builds the exact candidate, extracts 11 actual original/candidate methods into isolated synthetic fixtures, checks expected original failures against candidate behavior, and tests refusal cases. It never installs a DLL, starts Unity, opens a card/scene or changes configuration. [VALIDATION.md](VALIDATION.md) distinguishes these checks from real game acceptance.

The `tool` and `tests` projects are wrapper internals. Do not invoke their outputs to install anything. Only `candidate/TheBirdOfHermes.KKS.dll` is a potential deployment artifact; build tools, fixtures and reference DLLs must stay outside the game.

## Installation and rollback

Deployment is separate. With Studio stopped, verify the installed original hash, back it up, and replace only the plugin with the exact reviewed candidate. Preserve configuration and user audio/scene files. Confirm the loaded plugin version and test saved fade values through the real asynchronous restore path in a disposable scenario before claiming runtime repair.

For rollback, stop Studio and confirm the target still has the candidate hash. Restore the backed-up original. If it changed afterward, preserve that file and investigate before replacement. A DLL rollback does not undo later user scene or audio edits.

## Sources

The original is The Bird of Hermes 5.7.1 for KKS. Provenance and boundaries are in [SOURCES.md](SOURCES.md). No upstream binary, decompiled class, game reference, user content or private runtime log is included here.
