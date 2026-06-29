using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KSPClone.EditorTools
{
    /// <summary>
    /// Builds the headless dedicated-server binary (Standalone "Server"
    /// subtarget — no renderer) from the scenes enabled in Build Settings
    /// (NET-1, ADR-0008). Menu: KSPClone ▸ Build Dedicated Server. CLI:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod
    /// KSPClone.EditorTools.ServerBuilder.BuildFromCli -logFile build.log</c>.
    /// Requires the platform's Dedicated Server build module (Unity Hub).
    /// Output goes to Builds/Server (git-ignored).
    /// </summary>
    public static class ServerBuilder
    {
        private const string OutputDir = "Builds/Server";

        [MenuItem("KSPClone/Build Dedicated Server")]
        public static void BuildFromMenu() => Build();

        public static void BuildFromCli() => Build();

        private static void Build()
        {
            var scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[build] No enabled scenes in Build Settings — add the server scene first.");
                return;
            }

            var target = EditorUserBuildSettings.activeBuildTarget;
            Directory.CreateDirectory(OutputDir);
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, BinaryName(target)),
                target = target,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            };

            var summary = BuildPipeline.BuildPlayer(options).summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"[build] Dedicated server OK → {summary.outputPath} ({summary.totalSize} bytes)");
            else
                Debug.LogError($"[build] Dedicated server FAILED: {summary.result}, {summary.totalErrors} error(s)");
        }

        private static string[] EnabledScenes()
        {
            var list = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) list.Add(s.path);
            return list.ToArray();
        }

        private static string BinaryName(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows64 => "KSPCloneServer.exe",
            BuildTarget.StandaloneWindows => "KSPCloneServer.exe",
            BuildTarget.StandaloneLinux64 => "KSPCloneServer.x86_64",
            _ => "KSPCloneServer",
        };
    }
}
