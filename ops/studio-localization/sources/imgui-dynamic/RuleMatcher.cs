using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Codex.StudioImGuiDynamicBridge
{
    public sealed class RuleData
    {
        public string id, owner, pattern, replacement, source;
    }

    public sealed class RuleMatcher
    {
        public const int MaximumInput = 400;
        public const int MaximumExactInput = 12000;
        public const int MaximumCached = 2048;
        private sealed class Rule { internal RuleData Data; internal Regex Regex; internal bool TimedOut; internal string Prefix; }
        private struct CacheKey : IEquatable<CacheKey>
        {
            internal readonly string Owner, Input;
            internal readonly bool AllowGlobal;
            internal CacheKey(string owner, string input, bool allowGlobal)
            { Owner = owner; Input = input; AllowGlobal = allowGlobal; }
            public bool Equals(CacheKey other)
            {
                return AllowGlobal == other.AllowGlobal &&
                    string.Equals(Owner, other.Owner, StringComparison.Ordinal) &&
                    string.Equals(Input, other.Input, StringComparison.Ordinal);
            }
            public override bool Equals(object other) { return other is CacheKey && Equals((CacheKey)other); }
            public override int GetHashCode()
            {
                unchecked { return ((StringComparer.Ordinal.GetHashCode(Owner) * 397) ^
                    StringComparer.Ordinal.GetHashCode(Input)) * 397 ^ (AllowGlobal ? 1 : 0); }
            }
        }
        private sealed class Cached { internal CacheKey Key; internal string Value, RuleId; }
        private readonly Dictionary<string, List<Rule>> _rules = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, RuleData>> _exact = new Dictionary<string, Dictionary<string, RuleData>>(StringComparer.Ordinal);
        private readonly Dictionary<CacheKey, LinkedListNode<Cached>> _cache = new Dictionary<CacheKey, LinkedListNode<Cached>>();
        private readonly LinkedList<Cached> _lru = new LinkedList<Cached>();
        public int CachedCount { get { return _cache.Count; } }
        public int RuleCount { get; private set; }
        public int ExactCount { get; private set; }
        public long Hits { get; private set; }
        public long Evaluations { get; private set; }
        public int Timeouts { get; private set; }

        public RuleMatcher(IEnumerable<RuleData> rows)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuleData row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.owner) || string.IsNullOrEmpty(row.id) || row.replacement == null)
                    throw new ArgumentException("Invalid local text identity");
                if (row.source != null)
                {
                    if (row.source.Length == 0 || row.source.Length > MaximumExactInput) throw new ArgumentException("Exact local text length");
                    Dictionary<string,RuleData> exact;
                    if (!_exact.TryGetValue(row.owner,out exact)) {exact=new Dictionary<string,RuleData>(StringComparer.Ordinal);_exact.Add(row.owner,exact);}
                    RuleData previous;
                    if (exact.TryGetValue(row.source,out previous) && previous.replacement != row.replacement) throw new ArgumentException("Conflicting exact local text");
                    exact[row.source]=row;ExactCount++;continue;
                }
                if (row.pattern == null || !row.pattern.StartsWith(@"\A", StringComparison.Ordinal) ||
                    !row.pattern.EndsWith(@"\z", StringComparison.Ordinal) || row.pattern.Length > 8192)
                    throw new ArgumentException("Invalid anchored local rule");
                if (!seen.Add(row.owner + "\n" + row.pattern)) throw new ArgumentException("Duplicate owner rule");
                List<Rule> list;
                if (!_rules.TryGetValue(row.owner, out list)) { list = new List<Rule>(); _rules.Add(row.owner, list); }
                list.Add(new Rule { Data = row, Regex = new Regex(row.pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(10)), Prefix = LiteralPrefix(row.pattern) });
                RuleCount++;
            }
        }

        public bool HasOwner(string owner) { return owner != null && (_rules.ContainsKey(owner) || _exact.ContainsKey(owner)); }

        private static string LiteralPrefix(string pattern)
        {
            var prefix = new System.Text.StringBuilder();
            for(int index=2;index<pattern.Length;index++)
            {
                char c=pattern[index];
                if(c=='\\')
                {
                    if(index+1<pattern.Length && (" ().:+-[]{}?*|^$".IndexOf(pattern[index+1])>=0))
                    {prefix.Append(pattern[++index]);continue;}
                    break;
                }
                if("().[]{}?*+|^$".IndexOf(c)>=0)break;
                prefix.Append(c);
            }
            return prefix.ToString();
        }

        private bool Exact(string owner,string input,out string output,out string ruleId)
        {
            output=null;ruleId=null;Dictionary<string,RuleData> table;RuleData rule;
            if(owner==null||!_exact.TryGetValue(owner,out table)||!table.TryGetValue(input,out rule))return false;
            output=rule.replacement;ruleId=rule.id;return true;
        }

        public bool TryTranslate(string owner, string input, out string output, out string ruleId)
        {return TryTranslate(owner,input,false,out output,out ruleId);}

        public bool TryTranslate(string owner, string input, bool allowGlobal, out string output, out string ruleId)
        {
            output = null; ruleId = null;
            if (string.IsNullOrEmpty(input) || input.Length > MaximumExactInput || owner == null) return false;
            if(Exact(owner,input,out output,out ruleId))return output!=input;
            if(input.Length>MaximumInput)
                return allowGlobal&&owner!="global"&&Exact("global",input,out output,out ruleId)&&output!=input;
            var key = new CacheKey(owner, input, allowGlobal);
            LinkedListNode<Cached> cached;
            if (_cache.TryGetValue(key, out cached))
            {
                _lru.Remove(cached); _lru.AddFirst(cached); Hits++;
                output = cached.Value.Value; ruleId = cached.Value.RuleId;
                return output != null && output != input;
            }
            int ownerCount = allowGlobal && owner != "global" ? 2 : 1;
            for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
            {
                string sourceOwner = ownerIndex == 0 ? owner : "global";
                if(sourceOwner!=owner && Exact(sourceOwner,input,out output,out ruleId)) break;
                List<Rule> rules;
                if(!_rules.TryGetValue(sourceOwner,out rules))continue;
                for (int index = rules.Count - 1; index >= 0; index--)
                {
                    Rule rule = rules[index];
                    if (rule.TimedOut || !input.StartsWith(rule.Prefix,StringComparison.Ordinal)) continue;
                    try
                    {
                        Evaluations++;
                        Match match = rule.Regex.Match(input);
                        if (!match.Success) continue;
                        string translated = match.Result(rule.Data.replacement);
                        output = translated; ruleId = rule.Data.id;
                        break;
                    }
                    catch (RegexMatchTimeoutException) { rule.TimedOut = true; Timeouts++; }
                }
                if(output!=null)break;
            }
            var result = new Cached { Key = key, Value = output, RuleId = ruleId };
            var node = _lru.AddFirst(result); _cache.Add(key, node);
            if (_cache.Count > MaximumCached)
            {
                LinkedListNode<Cached> last = _lru.Last;
                _cache.Remove(last.Value.Key); _lru.RemoveLast();
            }
            return output != null && output != input;
        }
    }
}
