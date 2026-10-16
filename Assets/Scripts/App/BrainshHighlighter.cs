using UnityEngine;

namespace BrainshEditor
{
    /// <summary>
    /// Colours every character of a Brainsh source for the editor.
    /// The rules follow the official syntax definition of the language.
    /// </summary>
    public static class BrainshHighlighter
    {
        public static Color[] Colors(string text)
        {
            if (string.IsNullOrEmpty(text)) return new Color[0];

            var colors = new Color[text.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Ui.CodePlain;

            int depth = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Comments nest, exactly like the interpreter reads them.
                if (c == '{')
                {
                    depth++;
                    colors[i] = Ui.CodeComment;
                    continue;
                }

                if (c == '}')
                {
                    colors[i] = Ui.CodeComment;
                    if (depth > 0) depth--;
                    continue;
                }

                if (depth > 0)
                {
                    colors[i] = Ui.CodeComment;
                    continue;
                }

                if (c == ' ')
                {
                    colors[i] = Ui.CodePlain;
                    continue;
                }

                if (c == '\n' || c == '\t')
                {
                    colors[i] = Ui.CodePlain;
                    continue;
                }

                if (c == '%')
                {
                    int j = i + 1;
                    while (j < text.Length && text[j] != '\n') j++;

                    for (int k = i; k < j; k++) colors[k] = Ui.CodeInclude;
                    i = j - 1;
                    continue;
                }

                if (c == '@')
                {
                    colors[i] = Ui.CodeMacro;

                    int j = i + 1;
                    while (j < text.Length && IsNameChar(text[j])) j++;

                    for (int k = i + 1; k < j; k++) colors[k] = Ui.CodeMacro;

                    // "@name =" is a definition, "@name" is a call.
                    int sign = j;
                    while (sign < text.Length && (text[sign] == ' ' || text[sign] == '\t')) sign++;
                    if (sign < text.Length && text[sign] == '=') colors[sign] = Ui.CodeMacro;

                    i = j - 1;
                    continue;
                }

                if (c >= '0' && c <= '9')
                {
                    colors[i] = Ui.CodeNumber;
                    continue;
                }

                switch (c)
                {
                    case '>':
                    case '<':
                        colors[i] = Ui.CodeMove;
                        break;

                    case '+':
                    case '-':
                    case 'm':
                        colors[i] = Ui.CodeMath;
                        break;

                    case ':':
                    case ';':
                        colors[i] = Ui.CodeMath;
                        break;

                    case '.':
                    case ',':
                    case '#':
                        colors[i] = Ui.CodeIo;
                        break;

                    case 'N':
                    case 'S':
                    case 'd':
                        colors[i] = Ui.CodeKeyword;
                        break;

                    case 'r':
                        colors[i] = Ui.CodeRandom;
                        break;

                    case '[':
                    case ']':
                    case '(':
                    case ')':
                        colors[i] = Ui.CodeLoop;
                        break;

                    default:
                        colors[i] = Color.Lerp(Ui.CodePlain, Ui.Error, 0.75f);
                        break;
                }
            }

            return colors;
        }

        private static bool IsNameChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                   (c >= '0' && c <= '9') || c == '_';
        }
    }
}