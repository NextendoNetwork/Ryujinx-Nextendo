using SkiaSharp;
using System.Collections.Concurrent;
using System.Text;

namespace Ryujinx.HLE.UI
{
    /// <summary>
    /// Skia draws missing characters as boxes instead of falling back to another font,
    /// so text that the typeface can't fully draw (e.g. CJK) needs a system font picked for it.
    /// </summary>
    public static class TypefaceFallback
    {
        private static readonly ConcurrentDictionary<(string, int), SKTypeface> _fallbacks = new();

        /// <summary>
        /// Gets a typeface able to draw the given text: the typeface itself if it can,
        /// otherwise the system font that has the first character it lacks.
        /// </summary>
        /// <param name="typeface">Preferred typeface</param>
        /// <param name="text">Text to be drawn</param>
        /// <returns>The typeface to draw the text with</returns>
        public static SKTypeface For(SKTypeface typeface, string text)
        {
            if (string.IsNullOrEmpty(text) || typeface.ContainsGlyphs(text))
            {
                return typeface;
            }

            foreach (Rune rune in text.EnumerateRunes())
            {
                if (!typeface.ContainsGlyph(rune.Value))
                {
                    SKTypeface fallback = _fallbacks.GetOrAdd((typeface.FamilyName, rune.Value),
                        key => SKFontManager.Default.MatchCharacter(key.Item1, typeface.FontStyle, null, key.Item2));

                    return fallback ?? typeface;
                }
            }

            return typeface;
        }
    }
}
