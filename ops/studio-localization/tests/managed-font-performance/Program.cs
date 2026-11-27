using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Codex.StudioFontController;

internal static class Program
{
    private static int _checks;
    private static bool Available(char c) { return c != '\u25a1' && !char.IsSurrogate(c); }
    private static bool Expected(string text)
    {
        return text == null || text.All(c => char.IsWhiteSpace(c) || char.IsControl(c) || Available(c));
    }
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception(message);
    }
    private static void Main()
    {
        FontDefaultsRegression.Run(Check);
        int requests = 0;
        var coverage = new GlyphCoverage(c => { requests++; return Available(c); });
        foreach (string text in new[] { null, "", " \t\r\n", "宋体设置0123", "缺字\u25a1", "\ud840\udc00", "<b>中文</b>", new string('中', 513) })
            Check(coverage.Contains(text) == Expected(text), "coverage boundary");
        var random = new Random(1024);
        const string alphabet = "中文abc0123 \t\r\n\u25a1\ud840\udc00";
        for (int i = 0; i < 5000; i++)
        {
            string text = new string(Enumerable.Range(0, random.Next(1, 40)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            Check(coverage.Contains(text) == Expected(text), "random text");
            Check(coverage.Contains(new string(text.ToCharArray())) == Expected(text), "equal text new instance");
        }
        coverage.Contains("固定中文提示");
        int before = requests;
        for (int i = 0; i < 10000; i++) coverage.Contains("固定中文提示");
        Check(requests == before, "warm text called native glyph provider");
        for (int i = 0; i < 3000; i++) coverage.Contains("slot " + i);
        Check(coverage.CachedTextCount == GlyphCoverage.MaximumTexts, "unbounded cache");
        Check(coverage.Contains("固定中文提示"), "evicted text must still work");
        int count = coverage.CachedTextCount;
        Check(!coverage.Contains(new string('中', 600) + '\u25a1'), "long text fallback");
        Check(coverage.CachedTextCount == count, "long text retained");
        var differentFont = new GlyphCoverage(c => c != '中');
        Check(!differentFont.Contains("中文") && coverage.Contains("中文"), "cross-font contamination");

        string[] workload = Enumerable.Range(0, 64).Select(i => "工作室插件窗口中文说明：调整当前选项并保留原设置。" + new string('中', i % 40) + i).ToArray();
        var baseline = new PreviousCoverage();
        var optimized = new GlyphCoverage(Available);
        Func<string, bool> oldCall = baseline.Contains, newCall = optimized.Contains;
        for (int i = 0; i < 30000; i++) { oldCall(workload[i % workload.Length]); newCall(workload[i % workload.Length]); }
        var oldRuns = new List<Measurement>();
        var newRuns = new List<Measurement>();
        for (int run = 0; run < 5; run++)
        {
            if (run % 2 == 0) { oldRuns.Add(Measure(oldCall, workload)); newRuns.Add(Measure(newCall, workload)); }
            else { newRuns.Add(Measure(newCall, workload)); oldRuns.Add(Measure(oldCall, workload)); }
        }
        Check(newRuns.All(r => r.Bytes == 0), "warm text cache allocates");
        double oldMs = oldRuns.Select(r => r.Milliseconds).OrderBy(x => x).ElementAt(2);
        double newMs = newRuns.Select(r => r.Milliseconds).OrderBy(x => x).ElementAt(2);
        Console.WriteLine(JsonSerializer.Serialize(new {
            passed = true, checks = _checks, iterationsPerRun = 200000, runs = 5,
            runtime = Environment.Version.ToString(), tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            baselineMedianMs = oldMs, optimizedMedianMs = newMs, ratio = oldMs / newMs,
            optimizedAllocatedBytes = newRuns.Max(r => r.Bytes),
            limitation = "Synthetic repeated labels on .NET 10, not Unity Mono or complete font drawing; first-seen or changing text still requires glyph checks."
        }));
    }
    private sealed class Measurement { internal double Milliseconds; internal long Bytes; }
    private static Measurement Measure(Func<string, bool> call, string[] workload)
    {
        var watch = new Stopwatch();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        int successes = 0;
        for (int i = 0; i < 200000; i++) if (call(workload[i % workload.Length])) successes++;
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        if (successes != 200000) throw new Exception("benchmark translation changed");
        return new Measurement { Milliseconds = watch.Elapsed.TotalMilliseconds, Bytes = allocated };
    }
    private sealed class PreviousCoverage
    {
        private readonly Dictionary<char, bool> _characters = new Dictionary<char, bool>();
        internal bool Contains(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                bool found;
                if (!_characters.TryGetValue(c, out found)) { found = Available(c); _characters[c] = found; }
                if (!found) return false;
            }
            return true;
        }
    }
}
