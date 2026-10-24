using System.Globalization;
using System.Text.RegularExpressions;

namespace Th01.Assets.Text;

/// <summary>
/// The little part of C <c>printf</c> the game's own format strings use: <c>%s</c>, <c>%c</c> and <c>%[width]lu</c>
/// (numbers right-aligned in the field, space padded). Arguments are taken in order; anything else is left as it is.
/// </summary>
public static partial class PrintfLite
{
    [GeneratedRegex(@"%(\d*)(l?u|s|c)")]
    private static partial Regex Spec();

    public static string Format(string format, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(format);
        int next = 0;
        return Spec().Replace(format, m =>
        {
            if (next >= args.Length) return m.Value;
            object arg = args[next++];
            string text = arg switch
            {
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => arg.ToString() ?? "",
            };
            int width = m.Groups[1].Length > 0 ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            return m.Groups[2].Value.EndsWith('u') ? text.PadLeft(width) : text;
        });
    }
}
