using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace x2_prototype
{
    public sealed partial class TrimSuffixConverter : IValueConverter
    {
        // trim off "=XY" and "=XYZ" where X/Y/Z are numerical digits (including hexadecimal digits)
        private static readonly Regex Suffix = SuffixRegex();

        public object? Convert(
            object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string text)
            {
                return value;
            }

            text = Suffix.Replace(text, "").ToLower(culture);

            if (text.Length == 0)
            {
                return text;
            }

            return culture.TextInfo.ToUpper(text[..1]) + text[1..];
        }

        public object? ConvertBack(
            object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        [GeneratedRegex(@"\s*=\s*(?:\d{2,3}|0[xX][0-9A-Fa-f]{1,2})$")]
        private static partial Regex SuffixRegex();
    }
}
