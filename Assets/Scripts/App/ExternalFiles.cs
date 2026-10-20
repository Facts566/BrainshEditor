using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BrainshEditor
{
    /// <summary>
    /// A script the app was asked to open from the outside: a path on the command line
    /// (desktop) or an ACTION_VIEW intent from a file manager (Android). The text is
    /// copied into the scripts folder, so saving in the editor never overwrites the
    /// original file.
    /// </summary>
    public sealed class ExternalFile
    {
        /// <summary>File name the user opened, for example "notes.bsh".</summary>
        public string Name;

        public string Text;

        /// <summary>Where it came from, for the status line.</summary>
        public string Source;

        /// <summary>First readable .bsh file among the command line arguments.</summary>
        public static ExternalFile FromArguments(string[] args)
        {
            if (args == null) return null;

            // 0 is the executable itself.
            for (int i = 1; i < args.Length; i++)
            {
                string candidate = args[i];

                if (string.IsNullOrEmpty(candidate)) continue;
                if (candidate.StartsWith("-", StringComparison.Ordinal)) continue;
                if (!candidate.EndsWith(ScriptStore.Extension, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    if (!File.Exists(candidate)) continue;

                    return new ExternalFile
                    {
                        Name = Path.GetFileName(candidate),
                        Text = File.ReadAllText(candidate),
                        Source = Path.GetFullPath(candidate)
                    };
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Cannot open " + candidate + ": " + e.Message);
                }
            }

            return null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // The activity is started with launchMode="singleTask" and Unity's activity
        // calls setIntent() in onNewIntent(), so the same intent object stays current
        // when the app is already running.
        private static string _handledIntent;

        /// <summary>
        /// New ACTION_VIEW intent pointing at a file, or null when there is none.
        /// Every intent is returned once: the app may poll this every frame.
        /// </summary>
        public static ExternalFile FromIntent()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null) return null;

                    using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                    {
                        if (intent == null) return null;

                        string action = intent.Call<string>("getAction");
                        if (action != "android.intent.action.VIEW") return null;

                        using (var data = intent.Call<AndroidJavaObject>("getData"))
                        {
                            if (data == null) return null;

                            string uri = data.Call<string>("toString");
                            if (string.IsNullOrEmpty(uri) || uri == _handledIntent) return null;

                            // Marked before reading: an unreadable file must not be retried
                            // (and logged) on every poll.
                            _handledIntent = uri;

                            string text = Read(activity, data);
                            if (text == null) return null;

                            return new ExternalFile
                            {
                                Name = DisplayName(activity, data),
                                Text = text,
                                Source = uri
                            };
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Cannot open the file from the intent: " + e.Message);
                return null;
            }
        }

        private static string Read(AndroidJavaObject activity, AndroidJavaObject uri)
        {
            string scheme = uri.Call<string>("getScheme");

            if (scheme == "file")
            {
                string path = uri.Call<string>("getPath");
                return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null;
            }

            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var stream = resolver.Call<AndroidJavaObject>("openInputStream", uri))
            {
                if (stream == null) return null;

                using (var bytes = new MemoryStream())
                {
                    byte[] chunk = new byte[16384];
                    int read;

                    while ((read = stream.Call<int>("read", chunk)) > 0)
                        bytes.Write(chunk, 0, read);

                    return Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF');
                }
            }
        }

        private static string DisplayName(AndroidJavaObject activity, AndroidJavaObject uri)
        {
            try
            {
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var cursor = resolver.Call<AndroidJavaObject>("query", uri, null, null, null, null))
                {
                    if (cursor != null && cursor.Call<bool>("moveToFirst"))
                    {
                        int column = cursor.Call<int>("getColumnIndex", "_display_name");

                        if (column >= 0)
                        {
                            string name = cursor.Call<string>("getString", column);
                            if (!string.IsNullOrEmpty(name)) return name;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Providers without a display name: fall back to the last path segment.
            }

            string last = uri.Call<string>("getLastPathSegment");
            return string.IsNullOrEmpty(last) ? "opened.bsh" : last;
        }
#endif
    }
}
