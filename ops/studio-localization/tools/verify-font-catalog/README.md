# Explicit Windows installed-font verifier

Run this opt-in tool only when the user has authorized reading installed fonts. It reads Windows font registrations and the registered font files, then uses temporary native GDI memory DCs to verify the actual selected font. It never installs or deletes fonts, writes game configuration, opens game UI, or invokes Unity.

```powershell
dotnet build ops/studio-localization/tools/verify-font-catalog/FontCatalogVerifier.csproj -c Release --artifacts-path '<external evidence directory>/build'
dotnet '<external evidence directory>/build/bin/FontCatalogVerifier/release/FontCatalogVerifier.dll' --inspect-installed-fonts '<external evidence directory>/installed-fonts.json'
```

The report separates registry construction, background catalog construction, cached lookups, file reads and native probes. Rejected raw registrations are grouped by reason. Samples distinguish missing Chinese coverage from variable fonts and a requested face that does not match the file's first face.

The catalog is a startup snapshot. Restart Studio after installing or replacing system fonts. Its accepted fonts cover the fixed Chinese UI sample in both the file cmap and the GDI-selected face; this does not prove all Unicode characters or Unity rendering. Existing fallback fonts still handle missing characters during use.

Raw configuration identities remain unchanged. `Families`, `HasFamily` and `Resolve` preserve registered aliases. `ChineseFamilies` supplies a filtered, deduplicated choice list; `DisplayName` supplies cached Chinese labels. ConfigurationManager 19's standard string combobox ignores `ObjToStr`, so the controller must use its own setting drawer and avoid an `AcceptableValueList` that would clamp an old value to the first accepted choice. If a current saved value is outside the compatible list, retain it and show its status. An attempted application with an unverified or mismatched face is refused by `RegisteredFamily`.

Call `StartCatalogBuild` once when the resolver is created. Check `CatalogReady` and `CatalogError` before showing choices; `ChineseFamilies` does no I/O and returns an empty array until the completed snapshot is published. When ready, call `FilterRuntimeFamilies(Font.GetOSInstalledFontNames())` once on the Unity main thread and publish that returned choice list. The list and runtime loader use the same verified aliases, so a GDI candidate whose aliases are absent from Unity is removed. Further UI reads use the stored snapshot. The worker uses only file/registry-derived data and native GDI, with no Unity API calls. Per-font invalid data and access failures are isolated; unexpected worker errors leave the catalog explicitly incomplete.

Source policy:

- Read only the SFNT directory, first-face name table and cmap table using their file offsets.
- Require native Chinese glyph indices with `GGI_MARK_NONEXISTING_GLYPHS`; do not infer support from a Chinese-looking name.
- Compare the GDI-selected face's raw name table with the registered file's first face. A missing family silently substituted by GDI cannot pass this identity check.
- Keep TTC secondary faces out of the compatible list while Unity's current path loader selects the first face.
- Keep variable fonts and GDI/file face mismatches out until their weight/face behavior is verified across both Unity text systems.

Authoritative references:

- [GetGlyphIndicesW](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getglyphindicesw): unsupported glyphs are marked `0xffff` with the selected flag.
- [GetFontData](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getfontdata): retrieves tables for the selected face, including within a font collection.
- [GetFontUnicodeRanges](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getfontunicoderanges): reports font-supported ranges; used in the exploratory audit, not as a substitute for exact UI glyph checks.
- [OpenType name table](https://learn.microsoft.com/en-us/typography/opentype/spec/name) and [cmap](https://learn.microsoft.com/en-us/typography/opentype/spec/cmap): family identities and direct glyph mapping.
