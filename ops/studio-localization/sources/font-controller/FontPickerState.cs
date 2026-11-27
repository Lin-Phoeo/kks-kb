using System;
using System.Collections.Generic;

namespace Codex.StudioFontController
{
    internal sealed class FontPickerState
    {
        internal bool PendingExpanded, Expanded;
        internal string DraftQuery = "";
        internal bool Ready, Failed, CurrentCompatible;
        internal string CurrentLabel = "";
        internal string SelectedFamily = "";
        internal int[] Rows = new int[0];
        private string _query;
        private string[] _families;

        internal void Layout(string[] families, string[] labels, bool ready, bool failed, string current, string canonical = null)
        {
            Expanded = PendingExpanded;
            Ready = ready; Failed = failed;
            CurrentLabel = current ?? "";
            SelectedFamily = canonical ?? CurrentLabel;
            CurrentCompatible = ready && Array.IndexOf(families, SelectedFamily) >= 0;
            string query = (DraftQuery ?? "").Trim();
            if (ReferenceEquals(_families, families) && _query == query) return;
            _families = families; _query = query;
            Rows = Filter(families, labels, query);
        }

        internal static int[] Filter(string[] families, string[] labels, string query)
        {
            if (families.Length != labels.Length) throw new ArgumentException("Font display identities differ");
            query = (query ?? "").Trim();
            var rows = new List<int>();
            for (int index = 0; index < families.Length; index++)
                if (query.Length == 0 || families[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    labels[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) rows.Add(index);
            return rows.ToArray();
        }
    }
}
