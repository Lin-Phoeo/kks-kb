using System.Diagnostics;
using System.Text.Json;
using Codex.StudioFontController;

if (args.Length != 2 || args[0] != "--inspect-installed-fonts") throw new ArgumentException("Explicit opt-in required: --inspect-installed-fonts OUTPUT_JSON");
var watch = Stopwatch.StartNew();
var resolver = new FontSourceResolver(Path.GetTempPath());
string[] all = resolver.Families();
double registryMs = watch.Elapsed.TotalMilliseconds;
watch.Restart();
resolver.StartCatalogBuild();
while (!resolver.CatalogReady && resolver.CatalogError == null && watch.Elapsed.TotalSeconds < 60) Thread.Sleep(10);
if (!resolver.CatalogReady) throw new InvalidOperationException("Catalog did not complete: " + resolver.CatalogError);
string[] filtered = resolver.ChineseFamilies();
double catalogMs = watch.Elapsed.TotalMilliseconds;
watch.Restart();
for (int index = 0; index < 10000; index++) resolver.ChineseFamilies();
double cachedMs = watch.Elapsed.TotalMilliseconds;
var checks = new List<object>();
foreach (string family in new[] { "Microsoft YaHei", "Microsoft YaHei UI", "Microsoft JhengHei", "Microsoft JhengHei UI", "DengXian", "STXihei", "Noto Sans SC", "Noto Sans SC Medium", "Arial", "Segoe UI" })
{
    if (!resolver.HasFamily(family)) continue;
    string path = resolver.Resolve(family, "");
    var data = ChineseFontData.Read(path);
    var probe = ChineseFontProbe.Check(family, data);
    checks.Add(new { family, path, display = FontSourceResolver.DisplayName(family), probe.Supported, probe.Reason, probe.ActualFace,
        firstFaceNames = data.Names, data.Variable, data.HasChineseSample });
}
var registeredFamilies = new List<object>();
string[] simulatedUnityFamilies = all.Concat(filtered.Select(family => ChineseFontData.Read(resolver.Resolve(family, "")).PreferredFamily)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
filtered = resolver.FilterRuntimeFamilies(simulatedUnityFamilies);
foreach (string family in filtered)
{
    string path = resolver.Resolve(family, "");
    var data = ChineseFontData.Read(path);
    // Simulate Unity's list with registry aliases and actual English names. Every
    // returned name still needs its own native GDI identity/coverage proof.
    string registered;
    try { registered = resolver.RegisteredFamily(path, data.PreferredFamily, family, simulatedUnityFamilies); }
    catch (ArgumentException error) { throw new InvalidOperationException("Runtime family mapping failed: " + family + " -> " + data.PreferredFamily, error); }
    var probe = ChineseFontProbe.Check(registered, data);
    if (!probe.Supported) throw new InvalidOperationException("Runtime family mapping lost native proof: " + family);
    registeredFamilies.Add(new { requested = family, actualFirstFace = data.PreferredFamily, selectedLegacyFamily = registered, nativeSupported = probe.Supported });
}
var result = new { registeredFamilies = all.Length, filteredFamilies = filtered.Length, registryMs, catalogMs, backgroundBuildMs = resolver.CatalogMilliseconds,
    fileReads = resolver.CatalogFileReads, nativeProbes = resolver.CatalogNativeProbes, supportedAliases = resolver.CatalogSupportedAliases, rejections = resolver.CatalogRejections, cached10000CallsMs = cachedMs,
    families = filtered.Select(family => new { raw = family, display = FontSourceResolver.DisplayName(family) }).ToArray(), sampleChecks = checks,
    runtimeFamilyMapping = registeredFamilies,
    limitation = "Explicit read-only Windows installed-font verification. Runtime family mapping uses a simulated Unity OS-name list containing registry aliases and English first-face names, then validates every returned name with real GDI. Actual Unity may expose fewer aliases; unknown runtime names remain rejected. This does not call Unity or prove live rendering. No configuration changes or font installation. Chinese sample coverage does not prove all Unicode characters." };
File.WriteAllText(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { registeredFamilies = all.Length, filteredFamilies = filtered.Length, registryMs, catalogMs, backgroundBuildMs = resolver.CatalogMilliseconds,
    fileReads = resolver.CatalogFileReads, nativeProbes = resolver.CatalogNativeProbes, supportedAliases = resolver.CatalogSupportedAliases,
    runtimeFamilyMappings = registeredFamilies.Count, rejections = resolver.CatalogRejections, cached10000CallsMs = cachedMs }));
