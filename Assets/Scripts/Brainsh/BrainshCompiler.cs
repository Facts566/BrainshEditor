using System;
using System.Collections.Generic;

namespace Brainsh
{
    /// <summary>
    /// Turns Brainsh source text into a <see cref="BrainshProgram"/>.
    /// The pipeline mirrors the reference interpreter:
    /// comments -> includes -> macro definitions -> macro expansion -> validation.
    /// </summary>
    public static class BrainshCompiler
    {
        private const string ValidChars = "><+-.,[]()#NSdmr%:;@/0123456789 \n\t";

        public static BrainshProgram Compile(string source, IBrainshFileProvider files, string fileName = "main.bsh")
        {
            if (source == null) source = string.Empty;
            if (files == null) files = NullFileProvider.Instance;

            var fileNames = new List<string> { fileName };

            SourceText text = SourceText.FromText(source, 0);
            text = StripComments(text, fileNames);
            text = ProcessIncludes(text, files, fileNames, new HashSet<string> { fileName });

            SourceText body;
            Dictionary<string, SourceText> macros = ExtractMacros(text, fileNames, out body);

            SourceText code = ExpandMacros(body, macros, fileNames);

            return BuildProgram(code, fileNames);
        }

        /// <summary>Removes { ... } comments. Newlines are kept so line numbers stay stable.</summary>
        private static SourceText StripComments(SourceText src, List<string> fileNames)
        {
            var result = new SourceText();
            int depth = 0;
            int openLine = 0;
            int openFile = 0;

            for (int i = 0; i < src.Count; i++)
            {
                char c = src[i];
                int line = src.Lines[i];
                int file = src.Files[i];

                if (c == '\n')
                {
                    result.Add(c, line, file);
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        openLine = line;
                        openFile = file;
                    }

                    depth++;
                    continue;
                }

                if (c == '}')
                {
                    if (depth == 0)
                        throw Syntax("unexpected '}'", fileNames, file, line);

                    depth--;
                    continue;
                }

                if (depth == 0) result.Add(c, line, file);
            }

            if (depth > 0)
                throw Syntax("unclosed '{' (opened here)", fileNames, openFile, openLine);

            return result;
        }

        /// <summary>Inlines %file.bsh directives so their macros become available.</summary>
        private static SourceText ProcessIncludes(SourceText src, IBrainshFileProvider files,
            List<string> fileNames, HashSet<string> seen)
        {
            var result = new SourceText();
            int i = 0;

            while (i < src.Count)
            {
                char c = src[i];
                if (c != '%')
                {
                    result.Add(c, src.Lines[i], src.Files[i]);
                    i++;
                    continue;
                }

                int line = src.Lines[i];
                int file = src.Files[i];
                i++;

                string path;
                if (i < src.Count && src[i] == '"')
                {
                    int start = ++i;
                    while (i < src.Count && src[i] != '"' && src[i] != '\n') i++;

                    if (i >= src.Count || src[i] != '"')
                        throw Syntax("unclosed quote in the file name", fileNames, file, line);

                    path = Text(src, start, i);
                    i++;
                }
                else
                {
                    int start = i;
                    while (i < src.Count && !IsSpace(src[i])) i++;
                    path = Text(src, start, i);

                    for (int k = 0; k < path.Length; k++)
                        if (!IsPathChar(path[k]))
                            throw Syntax("invalid character '" + path[k] + "' in the file name (use quotes)",
                                fileNames, file, line);
                }

                if (path.Length == 0)
                    throw Syntax("a file name is required after '%'", fileNames, file, line);

                string full = path;
                string baseDir = DirectoryOf(fileNames[file]);
                if (!IsAbsolute(full) && baseDir.Length > 0)
                {
                    string candidate = baseDir + "/" + full;
                    if (files.TryReadFile(candidate, out _))
                        full = candidate;
                }

                if (seen.Contains(full))
                    continue;

                seen.Add(full);

                string content;
                if (!files.TryReadFile(full, out content))
                    throw new BrainshException(BrainshErrorKind.Io, "cannot open file: " + full, fileNames[file], line);

                fileNames.Add(full);

                SourceText included = SourceText.FromText(content, fileNames.Count - 1);
                included = StripComments(included, fileNames);
                included = ProcessIncludes(included, files, fileNames, seen);

                result.Append(included);
                result.Add('\n', line, file);
            }

            return result;
        }

        /// <summary>Pulls "@name = body" definitions out of the text; the body is the rest of the line.</summary>
        private static Dictionary<string, SourceText> ExtractMacros(SourceText src, List<string> fileNames,
            out SourceText result)
        {
            var macros = new Dictionary<string, SourceText>();
            result = new SourceText();

            int i = 0;
            while (i < src.Count)
            {
                if (src[i] != '@')
                {
                    result.Add(src[i], src.Lines[i], src.Files[i]);
                    i++;
                    continue;
                }

                int atLine = src.Lines[i];
                int atFile = src.Files[i];

                int start = ++i;
                while (i < src.Count && IsMacroChar(src[i])) i++;
                string name = Text(src, start, i);

                int j = i;
                while (j < src.Count && (src[j] == ' ' || src[j] == '\t')) j++;

                bool isDefinition = name.Length > 0 && j < src.Count && src[j] == '=';

                if (!isDefinition)
                {
                    // Not a definition: keep '@' and let the name be copied as ordinary code.
                    result.Add('@', atLine, atFile);
                    i = start;
                    continue;
                }

                j++;
                int bodyStart = j;
                while (j < src.Count && src[j] != '\n') j++;

                macros[name] = src.Slice(bodyStart, j);

                i = j;
                if (i < src.Count && src[i] == '\n') i++;
            }

            return macros;
        }

        /// <summary>Replaces every "@name" call with its body, guarding against self recursion.</summary>
        private static SourceText ExpandMacros(SourceText src, Dictionary<string, SourceText> macros,
            List<string> fileNames)
        {
            var result = new SourceText();
            ExpandInto(src, macros, new HashSet<string>(), result, fileNames);
            return result;
        }

        private static void ExpandInto(SourceText src, Dictionary<string, SourceText> macros, HashSet<string> active,
            SourceText result, List<string> fileNames)
        {
            int i = 0;

            while (i < src.Count)
            {
                if (src[i] != '@')
                {
                    result.Add(src[i], src.Lines[i], src.Files[i]);
                    i++;
                    continue;
                }

                int atLine = src.Lines[i];
                int atFile = src.Files[i];

                int start = ++i;
                while (i < src.Count && IsMacroChar(src[i])) i++;
                string name = Text(src, start, i);

                SourceText body;
                if (!macros.TryGetValue(name, out body))
                    throw Syntax("unknown macro @" + name, fileNames, atFile, atLine);

                if (active.Contains(name))
                    throw Syntax("macro @" + name + " calls itself", fileNames, atFile, atLine);

                active.Add(name);
                ExpandInto(body, macros, active, result, fileNames);
                active.Remove(name);
            }
        }

        private static BrainshProgram BuildProgram(SourceText src, List<string> fileNames)
        {
            int n = src.Count;

            var code = new char[n];
            var lineOf = new int[n];
            var fileOf = new int[n];
            var forward = new int[n];
            var backward = new int[n];

            for (int i = 0; i < n; i++)
            {
                char c = src[i];

                if (IsValidChar(c) == false)
                {
                    string shown = IsPrintable(c) ? c.ToString() : "\\x" + ((int)c).ToString("X2");
                    throw Syntax("invalid character '" + shown + "'", fileNames, src.Files[i], src.Lines[i]);
                }

                code[i] = c;
                lineOf[i] = src.Lines[i];
                fileOf[i] = src.Files[i];
                forward[i] = -1;
                backward[i] = -1;
            }

            var brackets = new Stack<int>();
            var parens = new Stack<int>();
            var openLine = new int[n];
            var openFile = new int[n];

            for (int i = 0; i < n; i++)
            {
                switch (code[i])
                {
                    case '[':
                        brackets.Push(i);
                        openLine[i] = lineOf[i];
                        openFile[i] = fileOf[i];
                        break;
                    case ']':
                        if (brackets.Count == 0)
                            throw Syntax("unexpected ']'", fileNames, fileOf[i], lineOf[i]);

                        int open = brackets.Pop();
                        forward[open] = i;
                        backward[i] = open;
                        break;
                    case '(':
                        parens.Push(i);
                        openLine[i] = lineOf[i];
                        openFile[i] = fileOf[i];
                        break;
                    case ')':
                        if (parens.Count == 0)
                            throw Syntax("unexpected ')'", fileNames, fileOf[i], lineOf[i]);

                        int popen = parens.Pop();
                        forward[popen] = i;
                        backward[i] = popen;
                        break;
                }
            }

            if (brackets.Count > 0)
            {
                int index = brackets.Peek();
                throw Syntax("unclosed '['", fileNames, openFile[index], openLine[index]);
            }

            if (parens.Count > 0)
            {
                int index = parens.Peek();
                throw Syntax("unclosed '('", fileNames, openFile[index], openLine[index]);
            }

            return new BrainshProgram
            {
                Code = new string(code),
                LineOf = lineOf,
                FileOf = fileOf,
                JumpForward = forward,
                JumpBack = backward,
                FileNames = fileNames.ToArray()
            };
        }

        private static BrainshException Syntax(string message, List<string> fileNames, int file, int line)
        {
            string name = file >= 0 && file < fileNames.Count ? fileNames[file] : null;
            return new BrainshException(BrainshErrorKind.Syntax, message, name, line);
        }

        private static string Text(SourceText src, int start, int end)
        {
            var chars = new char[end - start];
            for (int i = start; i < end; i++) chars[i - start] = src[i];
            return new string(chars);
        }

        private static bool IsValidChar(char c)
        {
            return ValidChars.IndexOf(c) >= 0;
        }

        private static bool IsPrintable(char c)
        {
            return c >= 32 && c < 127;
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }

        private static bool IsMacroChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
        }

        private static bool IsPathChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                   c == '.' || c == '_' || c == '-' || c == '/';
        }

        private static bool IsAbsolute(string path)
        {
            return path.Length > 0 && path[0] == '/';
        }

        private static string DirectoryOf(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            int slash = fileName.LastIndexOf('/');
            return slash < 0 ? string.Empty : fileName.Substring(0, slash);
        }

        private sealed class NullFileProvider : IBrainshFileProvider
        {
            public static readonly NullFileProvider Instance = new NullFileProvider();

            public bool TryReadFile(string path, out string content)
            {
                content = null;
                return false;
            }
        }
    }
}