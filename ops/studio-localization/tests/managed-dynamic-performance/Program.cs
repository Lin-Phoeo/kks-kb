using System.Diagnostics;
using System.Text.Json;
using Codex.StudioImGuiDynamicBridge;

string Option(string name, string fallback = null)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

var checks = new List<object>();
var failures = new List<string>();
void Check(string name, bool passed)
{
    checks.Add(new { name, passed });
    if (!passed) failures.Add(name);
}

RuleData Exact(string id, string owner, string source, string value) =>
    new RuleData { id = id, owner = owner, source = source, replacement = value };
RuleData RegexRule(string id, string owner, string pattern, string value) =>
    new RuleData { id = id, owner = owner, pattern = pattern, replacement = value };

void Expect(RuleMatcher matcher, string name, string owner, string input, bool allowGlobal,
    bool expectedChanged, string expectedValue, string expectedId)
{
    bool changed = matcher.TryTranslate(owner, input, allowGlobal, out string value, out string id);
    Check(name, changed == expectedChanged && value == expectedValue && id == expectedId);
}

var precedence = new RuleMatcher(new[] {
    Exact("plugin-exact", "A", "Exact", "local"),
    RegexRule("plugin-exact-regex", "A", @"\AExact\z", "regex"),
    Exact("global-exact", "global", "Exact", "global"),
    RegexRule("first", "A", @"\ASlot ([0-9]+)\z", "first $1"),
    RegexRule("last", "A", @"\ASlot (?<number>[0-9]+)\z", "local ${number}"),
    Exact("global-slot", "global", "Slot 1", "global slot"),
    Exact("identity", "A", "Slot 2", "Slot 2"),
    RegexRule("identity-regex", "A", @"\AIdentical\z", "$0"),
    Exact("identity-global", "global", "Identical", "must not win"),
    Exact("fallback-exact", "global", "Global", "global translated"),
    RegexRule("fallback-regex", "global", @"\AGlobal ([0-9]+)\z", "global $1"),
    RegexRule("escapes", "A", @"\AValue: (?<value>.+)\z", "译文 ${value}")
});
Expect(precedence, "plugin exact precedes all rules", "A", "Exact", true, true, "local", "plugin-exact");
Expect(precedence, "last plugin regex precedes global exact", "A", "Slot 1", true, true, "local 1", "last");
Expect(precedence, "plugin identity exact blocks fallback", "A", "Slot 2", true, false, "Slot 2", "identity");
Expect(precedence, "plugin identity regex blocks fallback", "A", "Identical", true, false, "Identical", "identity-regex");
Expect(precedence, "fallback disabled", "A", "Global", false, false, null, null);
Expect(precedence, "fallback enabled despite cached false mode", "A", "Global", true, true, "global translated", "fallback-exact");
Expect(precedence, "fallback remains disabled after enabled mode", "A", "Global", false, false, null, null);
Expect(precedence, "global regex fallback", "A", "Global 3", true, true, "global 3", "fallback-regex");
Expect(precedence, "global owner is not consulted twice", "global", "Global 3", true, true, "global 3", "fallback-regex");
Expect(precedence, "unknown owner may use allowed global fallback", "unknown", "Global", true, true, "global translated", "fallback-exact");
Expect(precedence, "null owner cannot use fallback", null, "Global", true, false, null, null);
Expect(precedence, "captured dollars and slashes remain literal", "A", @"Value: 原名 $1 [括号] \路径", false,
    true, @"译文 原名 $1 [括号] \路径", "escapes");

var boundaries = new RuleMatcher(new[] {
    RegexRule("owner-a", "a", @"\A(.+)\z", "a:$1"),
    RegexRule("owner-ab", "ab", @"\A(.+)\z", "ab:$1"),
    RegexRule("owner-a1", "a1", @"\A(.+)\z", "a1:$1"),
    RegexRule("owner-delimited", "a:1\nb", @"\A(.+)\z", "delimited:$1")
});
for (int repeat = 0; repeat < 2; repeat++)
{
    Expect(boundaries, "owner/input boundary a + bc " + repeat, "a", "bc", false, true, "a:bc", "owner-a");
    Expect(boundaries, "owner/input boundary ab + c " + repeat, "ab", "c", false, true, "ab:c", "owner-ab");
    Expect(boundaries, "flag-like owner suffix " + repeat, "a1", "x", false, true, "a1:x", "owner-a1");
    Expect(boundaries, "flag-like input prefix " + repeat, "a", "1x", true, true, "a:1x", "owner-a");
    Expect(boundaries, "delimiters in owner and input " + repeat, "a:1\nb", "0:xyz", false, true, "delimited:0:xyz", "owner-delimited");
}

var negative = new RuleMatcher(new[] { RegexRule("digits", "A", @"\AX([0-9]+)\z", "$1") });
Expect(negative, "initial negative query", "A", "Xno", false, false, null, null);
long evaluations = negative.Evaluations;
Expect(negative, "negative cache hit", "A", "Xno", false, false, null, null);
Check("negative cache does not reevaluate regex", negative.Hits == 1 && negative.Evaluations == evaluations);

string longExact = new string('x', 401);
string maximumExact = new string('y', 12000);
var lengths = new RuleMatcher(new[] {
    RegexRule("length-rule", "A", @"\Az+\z", "short"),
    Exact("long", "global", longExact, "long translated"),
    Exact("max", "A", maximumExact, "maximum translated")
});
Expect(lengths, "400 chars may use regex", "A", new string('z', 400), false, true, "short", "length-rule");
Expect(lengths, "401 chars do not use regex", "A", new string('z', 401), false, false, null, null);
Expect(lengths, "long exact with fallback disabled", "A", longExact, false, false, null, null);
Expect(lengths, "long exact may use allowed global fallback", "A", longExact, true, true, "long translated", "long");
Expect(lengths, "12000 chars exact accepted", "A", maximumExact, false, true, "maximum translated", "max");
Expect(lengths, "12001 chars rejected", "A", maximumExact + "y", false, false, null, null);
Expect(lengths, "empty rejected", "A", "", true, false, null, null);

var lru = new RuleMatcher(new[] { RegexRule("lru", "A", @"\AItem ([0-9]+)\z", "$1") });
for (int i = 0; i < RuleMatcher.MaximumCached; i++) lru.TryTranslate("A", "Item " + i, out _, out _);
lru.TryTranslate("A", "Item 0", out _, out _);
lru.TryTranslate("A", "Item 2048", out _, out _);
long hits = lru.Hits;
evaluations = lru.Evaluations;
lru.TryTranslate("A", "Item 0", out _, out _);
Check("recently used entry survives eviction", lru.Hits == hits + 1 && lru.Evaluations == evaluations);
lru.TryTranslate("A", "Item 1", out _, out _);
Check("least recent entry is reevaluated", lru.Evaluations == evaluations + 1);
Check("positive LRU remains bounded", lru.CachedCount == RuleMatcher.MaximumCached);
for (int i = 0; i < 4096; i++) negative.TryTranslate("A", "Xmiss " + i, out _, out _);
Check("negative LRU remains bounded", negative.CachedCount == RuleMatcher.MaximumCached);

var timeout = new RuleMatcher(new[] { RegexRule("timeout", "A", @"\A(a+)+\z", "bad") });
timeout.TryTranslate("A", new string('a', 399) + "!", out _, out _);
Check("catastrophic rule times out", timeout.Timeouts == 1);
evaluations = timeout.Evaluations;
timeout.TryTranslate("A", new string('a', 398) + "!", out _, out _);
Check("timed out rule is disabled", timeout.Timeouts == 1 && timeout.Evaluations == evaluations);

string corpusPath = Option("--rules");
int corpusEntries = 0;
if (corpusPath != null)
{
    RuleData[] corpus = JsonSerializer.Deserialize<RuleData[]>(File.ReadAllText(corpusPath), new JsonSerializerOptions { IncludeFields = true });
    var matcher = new RuleMatcher(corpus);
    foreach (RuleData rule in corpus.Where(rule => rule.source != null))
    {
        bool changed = matcher.TryTranslate(rule.owner, rule.source, false, out string translated, out _);
        Check("public exact " + rule.id, translated == rule.replacement && changed == (rule.source != rule.replacement));
        corpusEntries++;
    }
}

RuleMatcher BenchmarkMatcher() => new RuleMatcher(new[] {
    Exact("exact", "A", "Exact", "精确"),
    RegexRule("regex", "A", @"\ASlot ([0-9]+)\z", "槽位 $1"),
    Exact("fallback", "global", "Global", "全局")
});
var benchmarks = new List<object>();
void Measure(string name, string owner, string[] inputs, bool fallback, bool cold, bool allocationFree)
{
    const int repetitions = 5;
    int operations = cold ? inputs.Length : 200000;
    var samples = new List<(long bytes, long ticks)>();
    var warmup = BenchmarkMatcher();
    for (int i = 0; i < 20000; i++) warmup.TryTranslate(owner, inputs[i % inputs.Length], fallback, out _, out _);
    for (int sample = 0; sample < repetitions; sample++)
    {
        var matcher = cold ? BenchmarkMatcher() : warmup;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < operations; i++) matcher.TryTranslate(owner, inputs[i % inputs.Length], fallback, out _, out _);
        long ticks = Stopwatch.GetTimestamp() - start;
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        samples.Add((bytes, ticks));
    }
    double medianBytes = samples.OrderBy(sample => sample.bytes).ElementAt(repetitions / 2).bytes / (double)operations;
    double medianNanoseconds = samples.OrderBy(sample => sample.ticks).ElementAt(repetitions / 2).ticks * 1e9 / Stopwatch.Frequency / operations;
    benchmarks.Add(new { name, operations, repetitions, medianBytesPerOperation = medianBytes, medianNanosecondsPerOperation = medianNanoseconds });
    if (allocationFree && !args.Contains("--allow-allocations")) Check(name + " allocates zero bytes after warmup", medianBytes == 0);
}

Measure("exact-hit", "A", new[] { "Exact" }, false, false, true);
Measure("regex-cache-hit", "A", new[] { "Slot 1" }, false, false, true);
Measure("negative-cache-hit", "A", new[] { "Slot invalid" }, false, false, true);
Measure("global-fallback-cache-hit", "A", new[] { "Global" }, true, false, true);
Measure("cold-negative-eviction", "A", Enumerable.Range(0, 8192).Select(i => "unmatched " + i).ToArray(), false, true, false);

var result = new {
    label = Option("--label", "candidate"), passed = failures.Count == 0, checks = checks.Count, failures,
    corpusEntries, benchmarks,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
    scope = "Managed RuleMatcher only. Synthetic inputs and optional public exact corpus. Excludes Unity/Mono, Harmony hooks, cache owner reflection, GUI layout and rendering.",
    allocationPolicy = args.Contains("--allow-allocations") ? "baseline measured without allocation assertions" : "warm hit paths must allocate zero bytes on this .NET runtime"
};
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
string output = Option("--output");
if (output != null) File.WriteAllText(output, json);
Console.WriteLine(json);
return failures.Count == 0 ? 0 : 1;
