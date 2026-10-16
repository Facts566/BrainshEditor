using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Command line builds of the editor.
///
///   Unity -batchmode -quit -projectPath . -executeMethod AppBuildTool.BuildLinux
///   Unity -batchmode -quit -projectPath . -executeMethod AppBuildTool.BuildAndroid
/// </summary>
public static class AppBuildTool
{
    private const string ScenePath = "Assets/Scenes/App.unity";

    /// <summary>Objects of the first prototype, replaced by the interface built from code.</summary>
    private static readonly string[] PrototypeObjects = { "ui", "run_button", "code_input_field" };

    [MenuItem("Brainsh/Build Linux player")]
    public static void BuildLinux()
    {
        Build(BuildTarget.StandaloneLinux64, "Build/Linux/BrainshEditor.x86_64", BuildOptions.Development);
    }

    [MenuItem("Brainsh/Build Android APK")]
    public static void BuildAndroid()
    {
        // Switch the platform first: the Android settings are read when the target changes,
        // so setting them before the switch makes the build use the values of the old platform.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        PrepareAndroidSettings();

        // A cached Gradle project keeps the architecture list of an earlier build.
        string cache = Path.GetFullPath("Library/Bee/Android");
        if (Directory.Exists(cache)) Directory.Delete(cache, true);

        Build(BuildTarget.Android, "Build/Android/BrainshEditor.apk", BuildOptions.None);
    }

    /// <summary>
    /// Phone defaults: both phone architectures (modern devices are 64 bit only),
    /// a dark friendly status bar and no screen dimming while coding.
    /// </summary>
    public static void PrepareAndroidSettings()
    {
        PlayerSettings.Android.targetArchitectures =
            AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel22;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.forceSDCardPermission = false;
        PlayerSettings.Android.androidTVCompatibility = false;
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.brainshproject.brainsheditor");

        // Portrait first, the editor is built for a phone held in one hand.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        Debug.Log("ANDROID: architectures=" + PlayerSettings.Android.targetArchitectures +
                  " minSdk=" + PlayerSettings.Android.minSdkVersion +
                  " targetSdk=" + PlayerSettings.Android.targetSdkVersion +
                  " identifier=" + PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));

        AssetDatabase.SaveAssets();
    }

    /// <summary>Removes the leftover prototype objects and builds the player.</summary>
    public static void CleanAndBuildLinux()
    {
        CleanPrototypeScene();
        BuildLinux();
    }

    public static void CleanAndBuildAndroid()
    {
        CleanPrototypeScene();
        BuildAndroid();
    }

    [MenuItem("Brainsh/Remove prototype objects from the scene")]
    public static void CleanPrototypeScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var victims = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
            Collect(root.transform, victims);

        foreach (GameObject victim in victims) Object.DestroyImmediate(victim);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("CLEAN RESULT: removed " + victims.Count + " prototype objects");
    }

    private static void Collect(Transform transform, List<GameObject> victims)
    {
        for (int i = 0; i < transform.childCount; i++) Collect(transform.GetChild(i), victims);

        for (int i = 0; i < PrototypeObjects.Length; i++)
        {
            if (transform.name != PrototypeObjects[i]) continue;

            victims.Add(transform.gameObject);
            return;
        }
    }

    private static void Build(BuildTarget target, string path, BuildOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var settings = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            targetGroup = target == BuildTarget.Android ? BuildTargetGroup.Android : BuildTargetGroup.Standalone,
            options = options
        };

        BuildReport report = BuildPipeline.BuildPlayer(settings);
        BuildSummary summary = report.summary;

        Debug.Log("BUILD RESULT: " + summary.result +
                  " output=" + summary.outputPath +
                  " size=" + summary.totalSize +
                  " errors=" + summary.totalErrors +
                  " warnings=" + summary.totalWarnings);

        VerifyAndroidAbi(path);

        if (summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    /// <summary>Modern phones are 64 bit only, an APK without arm64 will not install on them.</summary>
    private static void VerifyAndroidAbi(string path)
    {
        if (!path.EndsWith(".apk") || !File.Exists(path)) return;

        bool arm64 = false;

        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        {
            foreach (var entry in zip.Entries)
                if (entry.FullName.StartsWith("lib/arm64-v8a/")) { arm64 = true; break; }
        }

        Debug.Log(arm64
            ? "ANDROID: the APK contains arm64, it installs on any modern phone"
            : "ANDROID WARNING: the APK has no arm64, it will not install on 64 bit only phones");
    }
}