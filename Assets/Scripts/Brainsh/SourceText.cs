using System.Collections.Generic;

namespace Brainsh
{
    /// <summary>
    /// Character buffer that remembers where every character came from (file + line),
    /// so runtime errors can point at the original source even after macros are expanded
    /// and files are inlined.
    /// </summary>
    internal sealed class SourceText
    {
        public readonly List<char> Chars = new List<char>();
        public readonly List<int> Lines = new List<int>();
        public readonly List<int> Files = new List<int>();

        public int Count => Chars.Count;
        public char this[int i] => Chars[i];

        public static SourceText FromText(string text, int file, int firstLine = 1)
        {
            var result = new SourceText();
            int line = firstLine;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Windows / old Mac line endings become a single '\n'.
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    c = '\n';
                }

                result.Add(c, line, file);
                if (c == '\n') line++;
            }

            return result;
        }

        public void Add(char c, int line, int file)
        {
            Chars.Add(c);
            Lines.Add(line);
            Files.Add(file);
        }

        public void Append(SourceText other)
        {
            Chars.AddRange(other.Chars);
            Lines.AddRange(other.Lines);
            Files.AddRange(other.Files);
        }

        /// <summary>Copy of the range [start, end).</summary>
        public SourceText Slice(int start, int end)
        {
            var result = new SourceText();
            for (int i = start; i < end; i++)
                result.Add(Chars[i], Lines[i], Files[i]);
            return result;
        }

        public string ToText()
        {
            return new string(Chars.ToArray());
        }
    }
}