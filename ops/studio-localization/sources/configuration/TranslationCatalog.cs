using System;
using System.Collections.Generic;

namespace Codex.StudioConfigLocalization
{
    public sealed class TranslationEntry
    {
        public string pluginGuid;
        public string kind;
        public string section;
        public string settingKey;
        public string source;
        public string value;
    }

    public sealed class TranslationCatalog
    {
        private readonly Dictionary<string, TranslationEntry> _entries = new Dictionary<string, TranslationEntry>(StringComparer.Ordinal);
        public IEnumerable<TranslationEntry> Entries { get { return _entries.Values; } }
        public int Count { get { return _entries.Count; } }

        public TranslationCatalog(IEnumerable<TranslationEntry> entries)
        {
            foreach (TranslationEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.pluginGuid) || string.IsNullOrEmpty(entry.kind) ||
                    entry.source == null || entry.value == null)
                    throw new ArgumentException("Translation entry lacks identity or text");
                if (entry.kind != "setting" && entry.kind != "description" && entry.kind != "category" && entry.kind != "header" && entry.kind != "enum")
                    throw new ArgumentException("Unknown translation kind");
                if ((entry.kind == "setting" || entry.kind == "description" || entry.kind == "enum") && (entry.section == null || entry.settingKey == null))
                    throw new ArgumentException("Setting translation lacks original definition");
                string key = Identity(entry.pluginGuid, entry.kind, entry.section, entry.settingKey, entry.source);
                TranslationEntry previous;
                if (_entries.TryGetValue(key, out previous) && previous.value != entry.value)
                    throw new ArgumentException("Conflicting translation identity");
                _entries[key] = entry;
            }
        }

        private static string Part(string value) { return (value ?? "").Length + ":" + (value ?? ""); }
        private static string NormalizeSource(string value)
        {
            return value == null ? null : value.Replace("\r\n", "\n").Replace("\r", "\n");
        }
        private static string Identity(string guid, string kind, string section, string key, string source)
        {
            return Part(guid) + Part(kind) + Part(section) + Part(key) + Part(NormalizeSource(source));
        }
        public bool TryGet(string guid, string kind, string section, string key, string source, out string value)
        {
            value = null;
            if (guid == null || source == null) return false;
            TranslationEntry entry;
            if (!_entries.TryGetValue(Identity(guid, kind, section, key, source), out entry)) return false;
            value = entry.value;
            return true;
        }
    }
}
