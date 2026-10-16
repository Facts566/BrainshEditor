using UnityEngine;

namespace BrainshEditor
{
    /// <summary>
    /// Creates the editor automatically, so any scene with a canvas just works.
    /// The AppController builds the rest of the interface from code.
    /// </summary>
    public static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateEditor()
        {
            if (Object.FindObjectOfType<AppController>() != null) return;

            var go = new GameObject("BrainshEditor");
            go.AddComponent<AppController>();
        }
    }
}