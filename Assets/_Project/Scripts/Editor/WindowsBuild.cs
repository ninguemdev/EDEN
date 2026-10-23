using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Eden.Editor
{
    /// <summary>
    /// Windows x64 player build from the menu or from the command line:
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.WindowsBuild.BuildFromCommandLine -logFile -
    /// </summary>
    public static class WindowsBuild
    {
        private const string OutputPath = "Builds/Windows/EDEN.exe";

        [MenuItem("EDEN/Build/Windows x64")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void BuildFromCommandLine()
        {
            var report = Build();
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static BuildReport Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"EDEN Windows build: {summary.result}, scenes={scenes.Length}, errors={summary.totalErrors}, " +
                      $"size={summary.totalSize} bytes, output={OutputPath}");
            return report;
        }
    }
}
