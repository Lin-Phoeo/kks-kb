# Validation scope

The regression fixture extracts production `SceneController.LoadV4`, `LoadV5`, `LoadV6` and eight `AudioTrack` methods: async byte loading, clearing, readiness/duration getters, trim/fade clamping and completion polling. The original and candidate run the same valid-audio assertions.

| Evidence | Result and boundary |
|---|---|
| Original comparison | 34 checks: 29 pass and 5 expected failures. V5/V6 fade values disappear before and after decode; oversized trim is not clamped at decode completion. |
| Candidate comparison | The same 34 checks pass. Covers V4 trim compatibility, V5/V6 fade restoration, incomplete/repeated polling, decode failure, clear, filtering, negative/oversized fades and oversized restored trims. |
| Change scope | Exactly two original methods change; 11,011 others and method identities remain, with no new type, method or AssemblyRef. |
| Exact artifacts | The wrapper verifies original and candidate SHA-256 and refuses unknown input, existing output, input/output reuse and unknown extraction input. |
| Isolation | Extracted fixtures use their own directory and `Probe.dll` identity, avoiding accidental binding to the unmodified fixture assembly. |
| Invalid zero-duration exploration | Reported separately, excluded from all pass/failure counts. The unchanged `ClampTrim` minimum-length formula can yield a negative trim when handed already-decoded zero-duration synthetic audio. This patch does not redefine that pre-existing invalid-input behavior. |
| Native behavior | Not proven by this offline suite. Decoder/device/Unity objects, waveform output and track allocation are synthetic boundaries. Real save-data restore, UI, playback, physical scene-file save/load and performance require separate runtime evidence. |

The synthetic decoder supplies completion or errors deterministically. It does not play sound or read user audio. The fixture contains newly authored stand-ins and loads extracted IL only from an explicitly supplied exact original/candidate; it does not load the whole game plugin for execution.

## Separate native Unity validation

The exact candidate subsequently passed 62 checks in an empty reference Studio. A newly authored silent WAV was decoded by the actual plugin. Production save-data capture and LoadV6 restored track names, offsets, trim, fades, lane metadata and deduplicated bytes in an isolated TrackManager; the original empty manager and configuration were preserved. Real AudioWindow.Draw Repaint calls were observed. The temporary observer was removed after normal process exit. See [runtime-validation.json](runtime-validation.json).

The upstream run first failed fade restoration (53/1). The first expanded candidate run restored fades but failed a strict nonnegative assertion on an existing float32 trim result of -9.313226E-09 (61/1). Independent arithmetic confirmed the rounding boundary; the final observation logged the value with a 1e-6 tolerance, without changing the candidate. These failed records remain distinct.

This does not prove physical scene PNG save/load, all formats, audible playback, VideoExport, every legacy native scene or long-session performance. Pre-existing console shutdown warnings remain outside this repair.
