using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Eden.Editor
{
    /// <summary>
    /// Windows x64 player build from the menu or from the command line, as a release build or as a Development build
    /// (profiler counters available, used by the performance measurement to count allocations):
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.WindowsBuild.BuildFromCommandLine -logFile -
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.WindowsBuild.BuildDevelopmentFromCommandLine -logFile -
    /// </summary>
    public static class WindowsBuild
    {
        private const string OutputPath = "Builds/Windows/EDEN.exe";
        private const string DevelopmentOutputPath = "Builds/WindowsDevelopment/EDEN.exe";

        [MenuItem("EDEN/Build/Windows x64")]
        public static void BuildFromMenu()
        {
            Build(false);
        }

        [MenuItem("EDEN/Build/Windows x64 (Development)")]
        public static void BuildDevelopmentFromMenu()
        {
            Build(true);
        }

        public static void BuildFromCommandLine()
        {
            var report = Build(false);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static void BuildDevelopmentFromCommandLine()
        {
            var report = Build(true);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static BuildReport Build(bool development)
        {
            var output = development ? DevelopmentOutputPath : OutputPath;
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"EDEN Windows build{(development ? " (Development)" : string.Empty)}: {summary.result}, " +
                      $"scenes={scenes.Length}, errors={summary.totalErrors}, size={summary.totalSize} bytes, output={output}");
            return report;
        }
    }
}
