using System;
using System.Collections.Generic;

namespace Codex.StudioFontController
{
    internal sealed class GlyphCoverage
    {
        internal const int MaximumTexts = 1024;
        internal const int MaximumTextLength = 512;
        private readonly Func<char, bool> _hasCharacter;
        private readonly Dictionary<char, bool> _characters = new Dictionary<char, bool>();
        private readonly Dictionary<string, LinkedListNode<Result>> _texts = new Dictionary<string, LinkedListNode<Result>>(StringComparer.Ordinal);
        private readonly LinkedList<Result> _recent = new LinkedList<Result>();
        private struct Result { internal string Text; internal bool Covered; }
        internal int CachedTextCount { get { return _texts.Count; } }

        internal GlyphCoverage(Func<char, bool> hasCharacter) { _hasCharacter = hasCharacter; }

        internal bool Contains(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            bool cacheable = text.Length <= MaximumTextLength;
            LinkedListNode<Result> cached;
            if (cacheable && _texts.TryGetValue(text, out cached))
            {
                if (cached != _recent.First) { _recent.Remove(cached); _recent.AddFirst(cached); }
                return cached.Value.Covered;
            }
            bool covered = true;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                bool found;
                if (!_characters.TryGetValue(c, out found)) { found = _hasCharacter(c); _characters[c] = found; }
                if (!found) { covered = false; break; }
            }
            if (cacheable)
            {
                // Keep both covered and fallback results, scoped to this font resource.
                var result = new Result { Text = text, Covered = covered };
                if (_texts.Count == MaximumTexts)
                {
                    cached = _recent.Last;
                    _texts.Remove(cached.Value.Text);
                    _recent.RemoveLast();
                    cached.Value = result;
                    _recent.AddFirst(cached);
                }
                else cached = _recent.AddFirst(result);
                _texts.Add(text, cached);
            }
            return covered;
        }
    }
}
