using System.IO;
using UnityEditor;
using UnityEngine;

public static class BatchBuild
{
    // The one to use for a normal build.
    //
    // SetupAndBuild()/ReleaseBuild() below both start with ProjectSetup.RunAll(),
    // which calls BuildScene() -- it deletes and regenerates Main.unity from
    // scratch, re-extracts the textures out of all five roster FBXs, and (via
    // CarRoster) wipes every saved car build with PlayerPrefs.DeleteAll(). That
    // is the right tool for bootstrapping the project from nothing, and much too
    // blunt for "compile what is already here".
    //
    // This path repairs the scene wiring in place (SceneUpgrade.Upgrade is
    // idempotent and checks before it acts), applies the release settings, and
    // builds. Minutes faster, and it cannot lose the roster.
    public static void QuickBuild()
    {
        const string scenePath = "Assets/Scenes/Main.unity";

        Debug.Log("=== BatchBuild: repairing scene wiring ===");
        SceneUpgrade.Upgrade();

        Debug.Log("=== BatchBuild: applying release configuration ===");
        ReleaseSetup.ConfigureRelease();

        if (!File.Exists(scenePath))
        {
            Debug.LogError("Scene not found at " + scenePath + ". Run BatchBuild.SetupAndBuild instead.");
            EditorApplication.Exit(1);
            return;
        }

        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = "Builds/BuildYourRideAR.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        });

        var summary = report.summary;
        Debug.Log("=== BatchBuild: " + summary.result +
                  ", " + (summary.totalSize / (1024 * 1024)) + " MB" +
                  ", " + summary.totalErrors + " error(s) ===");

        // -batchmode does not fail the shell on a failed build unless the exit
        // code says so, which is how a broken build gets mistaken for a good one
        // in a script.
        EditorApplication.Exit(summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    // Invoked via -executeMethod from the command line.
    public static void SetupAndBuild()
    {
        Debug.Log("=== BatchBuild: running full setup ===");
        ProjectSetup.RunAll();
        Debug.Log("=== BatchBuild: building APK ===");
        ProjectSetup.BuildApk();
    }

    // Full release pipeline: setup, release config (version/icon/signing),
    // then both a sideloadable APK and a Play Store AAB.
    public static void ReleaseBuild()
    {
        Debug.Log("=== BatchBuild: running full setup ===");
        ProjectSetup.RunAll();
        Debug.Log("=== BatchBuild: configuring release ===");
        ReleaseSetup.ConfigureRelease();
        Debug.Log("=== BatchBuild: building APK ===");
        ProjectSetup.BuildApk();
        Debug.Log("=== BatchBuild: building AAB ===");
        ReleaseSetup.BuildAab();
    }
}
