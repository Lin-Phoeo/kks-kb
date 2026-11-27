using System;

namespace Codex.StudioFontController
{
    internal static class TypographyPolicy
    {
        internal const int MinimumDelta = -2, MaximumDelta = 3;
        internal const int MinimumSize = 12;
        internal static int Delta(int value) { return Math.Max(MinimumDelta, Math.Min(MaximumDelta, value)); }
        internal static int Style(int original, bool bold) { return bold ? original | 1 : original; }
        internal static float Size(float original, int delta, float ceiling = float.PositiveInfinity)
        {
            if (delta == 0 || original <= 0) return original;
            float floor = Math.Min(original, MinimumSize);
            return Math.Min(Math.Max(original, ceiling), Math.Max(floor, original + Delta(delta)));
        }
        internal static TypographyValue Adjust(TypographyValue original, bool bold, int delta, float ceiling = float.PositiveInfinity)
        {
            var result = original;
            result.Style = Style(original.Style, bold);
            result.Size = Size(original.Size, delta, ceiling);
            result.Minimum = Size(original.Minimum, delta, ceiling);
            result.Maximum = Math.Max(result.Minimum, Size(original.Maximum, delta, ceiling));
            return result;
        }
    }

    internal struct TypographyValue
    {
        internal float Size, Minimum, Maximum;
        internal int Style;
        internal bool Automatic;
    }

    internal sealed class TypographyState
    {
        internal TypographyValue Original;
        private TypographyValue _last;
        private bool _applied;
        internal TypographyState(TypographyValue original) { Original = original; }
        internal TypographyState Copy() { return new TypographyState(Original) { _last = _last, _applied = _applied }; }
        internal void ExternalSize(float value, float current) { if (value != current) { Original.Size = value; _last.Size = value; } }
        internal void ExternalMinimum(float value, float current) { if (value != current) { Original.Minimum = value; _last.Minimum = value; } }
        internal void ExternalMaximum(float value, float current) { if (value != current) { Original.Maximum = value; _last.Maximum = value; } }
        internal void ExternalStyle(int value, int current) { if (value != current) { Original.Style = value; _last.Style = value; } }
        internal void ExternalAutomatic(bool value, bool current) { if (value != current) { Original.Automatic = value; _last.Automatic = value; } }
        private void Observe(TypographyValue current)
        {
            if (!_applied) { Original = current; return; }
            if (current.Style != _last.Style) Original.Style = current.Style;
            if (current.Automatic != _last.Automatic) Original.Automatic = current.Automatic;
            if (current.Minimum != _last.Minimum) Original.Minimum = current.Minimum;
            if (current.Maximum != _last.Maximum) Original.Maximum = current.Maximum;
            // TMP writes its fitted size while laying out auto-sized text.
            if (current.Size != _last.Size && !current.Automatic) Original.Size = current.Size;
        }
        internal TypographyValue Apply(TypographyValue current, bool bold, int delta, float ceiling = float.PositiveInfinity)
        {
            Observe(current);
            _last = TypographyPolicy.Adjust(Original, bold, delta, ceiling);
            _applied = true;
            return _last;
        }
        internal TypographyValue Restore(TypographyValue current)
        {
            Observe(current);
            _applied = false;
            return Original;
        }
    }

    internal sealed class TypographyScope
    {
        internal int Depth { get; private set; }
        internal TypographyValue Original { get; private set; }
        internal bool Enter(TypographyValue current)
        {
            if (Depth++ != 0) return false;
            Original = current;
            return true;
        }
        internal bool Exit() { return Depth > 0 && --Depth == 0; }
    }
}
