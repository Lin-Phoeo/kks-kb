# Verification scope

| Evidence | Result and boundary |
|---|---|
| Actual cold Studio observation | Original DragDoll loads, then its enhanced-axis bridge rejects the installed basic 2.0 before registering bridge patches. This is the trigger; original log stays private. |
| Exact binary patch | One original method changed; 1,459 original methods and runtime references retained. Candidate hash fixed in the tool and wrapper. |
| Actual Tick comparison | 10 runs, 34 checks: original known-base error reproduced; candidate known-base informational skip; complete enhanced contract retains two direct patches and four secondary installer calls; unknown missing contract still errors; absence still waits. |
| Actual disk identity helper | Locally verified against the installed 2.0 file and separately archived 3.1 file: only exact 2.0 matched. Only types/identity were resolved; no lifecycle or Unity native calls. |
| Refusal checks | Unknown DragDoll, unknown Axis, existing output and input/output reuse are rejected; input/candidate bytes remain unchanged. |
| Runtime candidate acceptance | Pending. No active pose, guide manipulation, Timeline playback or complete enhanced plugin was exercised by the fixtures. |

The synthetic fixture substitutes assembly discovery and the file-hash read to create controlled basic/enhanced/unknown cases. Every known-build acceptance still passes through the real helper name/hash comparison. It does not execute any game code during normal automated repository tests; the explicit wrapper requires local input paths. The source tests themselves are synthetic; extracted binaries remain external and private.
