# ConfigurationManager 19.1 compatibility candidate

ConfigurationManager 19.1 adds translated text measurement, flexible range-value widths, WindowSkin and tooltip drawing through IMGUI hooks. The configuration display and AniMorph layout helpers both pin the manager binary; replacing the manager alone disables those helpers.

This candidate keeps the exact hash check and changes it to the reviewed BepInEx 5 build of 19.1. The configuration helper becomes 1.0.2 and the AniMorph helper becomes 1.0.1. Their method bodies each differ by one expected-hash string; their translation, restoration and width policies remain unchanged. The release manifest requires that exact 19.1 build for these components. Published 0.3.0-rc.2 remains the previous 19.0 rollback path; this change does not republish it or upgrade ConfigurationManager automatically.

Current XUnity Core already derives from official 5.6.3.1 with the one-instruction Type selection repair. Rebuilding the existing patch reproduces the accepted Core hash. Its older BepInEx entry version label is not evidence that the engine is still 5.6.2. Do not replace the repaired Core, HiDPI callback adapter, font controller or translation dictionaries during this manager upgrade.

## Validation boundary

Cecil comparison preserves all eight display-hook signatures and the ten observed direct ConfigurationManager member references from existing consumers. Thirty-three managed display/identity/transpiler tests pass against real 19.1 and the candidate helper with synthetic settings; the current catalog parses. The AniMorph RightColumnWidth getter remains available. These checks do not prove native Unity layout, font metrics, callback ownership or full application lifecycle.

A coordinated empty-Studio run completed 45 checks with no failures against the exact three candidate files. The real F1 window drew five temporary search states: empty, BetterSceneLoader, no results, Studio Font, and empty again. Repaint observations confirmed noncollapsed search fields, bilingual plugin headers, Chinese setting labels, the active ConfigurationManager translation owner and the HiDPI callback route. The window, search text, cursor and configuration hashes were restored. Studio then exited after two normal close requests; pre-existing console shutdown warnings remain separate.

This does not verify physical typing, enum dropdown interaction, tooltip pixels, scrolling, expand/collapse, font switching, AniMorph row width, slider clipping, PerspectiveX's window or every plugin combination. Those should remain explicit follow-up checks. The native test changed no font preference or ConfigEntry. Roll back the manager and both helpers together; preserve user configuration and dictionaries. Generated DLL hashes are build artifacts and must be independently verified after rebuilding with another toolchain or path.

A new distributable must use a new version identifier such as 0.3.0-rc.3-cm19.1; do not overwrite or republish the prior rc.2 artifact. The package builder now defaults to 0.3.0-rc.3-cm19.1; release automation should still pass --version explicitly.
