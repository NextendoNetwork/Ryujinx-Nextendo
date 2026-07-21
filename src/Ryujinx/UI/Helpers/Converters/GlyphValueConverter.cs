using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;

namespace Ryujinx.Ava.UI.Helpers
{
    public class GlyphValueConverter : MarkupExtension
    {
        private readonly string _key;

        private static readonly Dictionary<Glyph, string> _glyphs = new()
        {
            { Glyph.List, char.ConvertFromUtf32((int)Symbol.List) },
            { Glyph.Grid, char.ConvertFromUtf32((int)Symbol.ViewAll) },
            { Glyph.Chip, char.ConvertFromUtf32(59748) },
            { Glyph.Device, char.ConvertFromUtf32(0xE7F7) },
            { Glyph.Bug, char.ConvertFromUtf32(0xEBE8) },
            { Glyph.Important, char.ConvertFromUtf32((int)Symbol.Important) },
        };

        public GlyphValueConverter(string key)
        {
            _key = key;
        }

        public string this[string key] =>
            _glyphs.TryGetValue(Enum.Parse<Glyph>(key), out string val)
                ? val
                : string.Empty;

        public override object ProvideValue(IServiceProvider serviceProvider) => this[_key];
    }
}
