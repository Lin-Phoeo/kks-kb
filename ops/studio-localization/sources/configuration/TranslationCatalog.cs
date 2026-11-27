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
        private readonly Dictionary<TranslationIdentity, TranslationEntry> _entries = new Dictionary<TranslationIdentity, TranslationEntry>();
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
                var key = new TranslationIdentity(entry.pluginGuid, entry.kind, entry.section, entry.settingKey, entry.source);
                TranslationEntry previous;
                if (_entries.TryGetValue(key, out previous) && previous.value != entry.value)
                    throw new ArgumentException("Conflicting translation identity");
                _entries[key] = entry;
            }
        }

        private static string NormalizeSource(string value)
        {
            return value.IndexOf('\r') < 0 ? value : value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private readonly struct TranslationIdentity : IEquatable<TranslationIdentity>
        {
            private readonly string _guid, _kind, _section, _key, _source;

            internal TranslationIdentity(string guid, string kind, string section, string key, string source)
            {
                _guid = guid ?? "";
                _kind = kind ?? "";
                _section = section ?? "";
                _key = key ?? "";
                _source = NormalizeSource(source);
            }

            public bool Equals(TranslationIdentity other)
            {
                return _guid == other._guid && _kind == other._kind && _section == other._section &&
                    _key == other._key && _source == other._source;
            }

            public override bool Equals(object value)
            {
                return value is TranslationIdentity && Equals((TranslationIdentity)value);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.Ordinal.GetHashCode(_guid);
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(_kind);
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(_section);
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(_key);
                    return hash * 31 + StringComparer.Ordinal.GetHashCode(_source);
                }
            }
        }
        public bool TryGet(string guid, string kind, string section, string key, string source, out string value)
        {
            value = null;
            if (guid == null || source == null) return false;
            TranslationEntry entry;
            if (!_entries.TryGetValue(new TranslationIdentity(guid, kind, section, key, source), out entry)) return false;
            value = entry.value;
            return true;
        }
    }
}
