# Synthetic font catalog checks

This test executable uses generated in-memory SFNT, TTC, name and cmap tables. Its catalog probes are injected delegates. It does not enumerate Windows fonts, read an installed font or game installation, or call native GDI.

Build and run with external artifacts:

```powershell
dotnet build ops/studio-localization/tests/managed-font-catalog/FontCatalogTests.csproj -c Release --artifacts-path '<external output directory>'
dotnet '<external output directory>/bin/FontCatalogTests/release/FontCatalogTests.dll'
```

Checks cover actual character maps independently of family spelling, format 4 and 12 glyph handling, truncated input, TTC first-face identity, missing Unicode names, per-file parse caching, per-alias native-result caching, rejected font isolation, and accurate Chinese display names without changing raw identities.

The explicit installed-font verifier lives under `tools/verify-font-catalog` and must not be added to normal automated tests.
