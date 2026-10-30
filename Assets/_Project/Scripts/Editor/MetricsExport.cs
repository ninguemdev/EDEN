using System;
using Eden.Persistence;
using UnityEditor;

namespace Eden.Editor
{
    public static class MetricsExport
    {
        public static void FromCommandLine()
        {
            try
            {
                var args = Environment.GetCommandLineArgs(); string source = null, output = null;
                for (var i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-eden-source") source = args[++i];
                    else if (args[i] == "-eden-output") output = args[++i];
                if (source == null || output == null) throw new ArgumentException("Use -eden-source DIR -eden-output NEW_DIR.");
                MetricsCompatibilityExporter.Export(source, output);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { UnityEngine.Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
