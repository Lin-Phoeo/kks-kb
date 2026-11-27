# Validation scope

| Evidence | Result |
|---|---|
| Exact generation | Each component changes only EnsureInstalled and its displayed local version; all other original methods and references remain. |
| Actual-CIL comparisons | 16 runs / 178 checks. Current original gate refusal reproduced; candidate current identity accepted; original 1.5.1.1 route unchanged; unknown same-version hash and wrong GUID rejected. |
| Guard behavior | Actual installed PseudoMaker UpdateValue/ClampValue/coordinate-trim CIL plus actual Vanity prefixes: passive refresh retains an existing out-of-range value, interactive bounds stay enabled, reserved tail slots remain, ordinary trimming still runs. |
| Registration frequency | Repeating EnsureInstalled after successful registration or rejection makes no additional file-hash reads in the fixture. |
| Cleanup branch | Exact Free host metadata has no cleanup authority type. Actual LegacyOutfitCleanupBackend.TryCreate CIL returns false before constructor/backend initialization when the type is absent. |
| Actual helper disk identity | Local check accepts the precise installed 1.5.2.0 SHA, preserves the old supported branch, and rejects future version/missing assembly. No target types or lifecycle run. |
| Output safeguards | Unknown Vanity, unknown PseudoMaker, existing output and input/output reuse refused; sources unchanged. |
| Real game behavior | Not established by these checks. No true UI editing, card/coordinate write, native runtime patch registration or cleanup movement was performed. |

The extraction tool substitutes assembly discovery, disk-hash input, Harmony Install recorder, Unity UI and reservation-count storage. The actual original methods otherwise run with synthetic inputs; the full weak-reference reservation implementation and real lifecycle are outside these fixtures. The source test project uses synthetic types and contains no game files. Extracted artifacts require the explicit wrapper and remain local.
