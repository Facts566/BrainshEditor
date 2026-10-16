using System;
using System.Text;
using UnityEngine;

namespace BrainshEditor
{
    /// <summary>
    /// Turns coloured characters into the rich text a Text component understands.
    /// The editor draws plain Text, so the syntax colours travel as colour tags.
    ///
    /// Brainsh is made of '&lt;' and '&gt;', and their glyphs are not as wide as the look
    /// alike characters used for escaping. So the real characters are kept and only a
    /// genuine tag is escaped - otherwise the caret slowly drifts away from the text.
    /// </summary>
    public static class BrainshMarkup
    {
        private const string Open = "<color=#";
        private const string Close = "</color>";

        private static readonly string[] Tags =
        {
            "color", "color32", "b", "i", "u", "s", "size", "font", "material",
            "space", "sup", "sub", "mark", "rotate", "sprite", "noparsing"
        };

        public static string ToRichText(string text, Color[] colors)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length + 64);
            Color current = Color.white;
            bool open = false;

            for (int i = 0; i < text.Length; i++)
            {
                Color color = colors != null && i < colors.Length ? colors[i] : Color.white;

                if (!open || color != current)
                {
                    if (open) sb.Append(Close);

                    sb.Append(Open).Append(ColorUtility.ToHtmlStringRGB(color)).Append('>');
                    current = color;
                    open = true;
                }

                sb.Append(Escape(text, i));
            }

            if (open) sb.Append(Close);

            return sb.ToString();
        }

        private static string Escape(string text, int i)
        {
            char c = text[i];

            if (c == '<' && StartsTag(text, i)) return "\u2039";
            if (c < 32 && c != '\n' && c != '\t') return ".";

            return c.ToString();
        }

        /// <summary>True when a rich text tag starts at this position.</summary>
        private static bool StartsTag(string text, int i)
        {
            int j = i + 1;

            if (j < text.Length && text[j] == '/') j++;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;

            foreach (string tag in Tags)
            {
                if (j + tag.Length > text.Length) continue;
                if (string.Compare(text, j, tag, 0, tag.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;

                char after = j + tag.Length < text.Length ? text[j + tag.Length] : ' ';
                if (char.IsLetterOrDigit(after)) continue;

                return true;
            }

            return false;
        }
    }
}
