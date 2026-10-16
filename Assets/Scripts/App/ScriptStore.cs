using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Brainsh;
using UnityEngine;

namespace BrainshEditor
{
    /// <summary>
    /// The user's scripts on disk. It also resolves the %include directive,
    /// so a program can pull macros out of a neighbouring file.
    /// </summary>
    public sealed class ScriptStore : IBrainshFileProvider
    {
        public const string Extension = ".bsh";

        public string Root { get; private set; }

        public ScriptStore() : this(Path.Combine(ApplicationPaths.PersistentData, "scripts"))
        {
        }

        public ScriptStore(string root)
        {
            Root = root;

            try
            {
                Directory.CreateDirectory(Root);
            }
            catch (Exception)
            {
                // A read only location is reported later, when a script is saved.
            }
        }

        /// <summary>File names in the store, without the extension.</summary>
        public List<string> List()
        {
            var names = new List<string>();

            try
            {
                foreach (string file in Directory.GetFiles(Root, "*" + Extension))
                    names.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch (Exception)
            {
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public bool Exists(string name)
        {
            return File.Exists(PathFor(name));
        }

        public string Read(string name)
        {
            try
            {
                return Normalize(File.ReadAllText(PathFor(name)));
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public bool Write(string name, string text)
        {
            try
            {
                string path = PathFor(name);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, Normalize(text ?? string.Empty));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Cannot save " + name + ": " + e.Message);
                return false;
            }
        }

        public bool Delete(string name)
        {
            try
            {
                string path = PathFor(name);
                if (!File.Exists(path)) return false;

                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Name for a new script that does not exist yet.</summary>
        public string NextName(string preferred = "script")
        {
            preferred = Sanitize(preferred);
            if (preferred.Length == 0) preferred = "script";

            if (!Exists(preferred + Extension)) return preferred + Extension;

            for (int i = 2; i < 1000; i++)
                if (!Exists(preferred + i + Extension)) return preferred + i + Extension;

            return preferred + "_" + Guid.NewGuid().ToString("N").Substring(0, 4) + Extension;
        }

        /// <summary>Resolves an include path inside the store. Paths cannot escape it.</summary>
        public bool TryReadFile(string path, out string content)
        {
            content = null;

            if (string.IsNullOrEmpty(path)) return false;

            string full;
            try
            {
                string root = Path.GetFullPath(Root);
                full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

                if (!full.StartsWith(root, StringComparison.Ordinal)) return false;
                if (!File.Exists(full)) return false;

                content = Normalize(File.ReadAllText(full));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string PathFor(string name)
        {
            string safe = Sanitize(name);
            if (!safe.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) safe += Extension;

            return Path.Combine(Root, safe);
        }

        /// <summary>Strips anything that could escape the store.</summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var sb = new StringBuilder(name.Length);

            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') sb.Append(c);
                else if (c == ' ' || c == '/') sb.Append('_');
            }

            string result = sb.ToString();

            // No path tricks, no hidden files.
            while (result.Contains("..")) result = result.Replace("..", ".");
            if (result.StartsWith(".", StringComparison.Ordinal)) result = result.Substring(1);

            return result.Trim();
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }

    internal static class ApplicationPaths
    {
        public static string PersistentData
        {
            get { return UnityEngine.Application.persistentDataPath; }
        }
    }
}