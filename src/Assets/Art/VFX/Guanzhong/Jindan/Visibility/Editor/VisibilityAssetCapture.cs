using System;
using System.IO;
using TianZhang.Art.Jindan.Visibility.Samples;
using TianZhang.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Art.Jindan.Visibility.Editor
{
    public static class VisibilityAssetCapture
    {
        private const string Active = "D12VisibilityCaptureActive";
        private static double started;

        public static void Capture1920() => Begin(1920);
        public static void Capture1366() => Begin(1366);

        public static void ProbeDestruction()
        {
            EditorSceneManager.OpenScene(VisibilityAssetBuilder.Root + "/Samples/VisibilityAssetSample.unity");
            UnityEngine.Object.FindFirstObjectByType<VisibilityAssetSample>().ProbeDestruction();
        }
        private static void Begin(int width)
        {
            EditorSceneManager.OpenScene(VisibilityAssetBuilder.Root + "/Samples/VisibilityAssetSample.unity");
            var sample = UnityEngine.Object.FindFirstObjectByType<VisibilityAssetSample>();
            sample.capture = true; sample.captureWidth = width;
            SessionState.SetBool(Active, true);
            Attach();
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Attach()
        {
            if (!SessionState.GetBool(Active, false)) return;
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            File.WriteAllText(Path.Combine(VisibilityAssetBuilder.Evidence, "capture-error.txt"), condition + "\n" + stackTrace);
            SessionState.SetBool(Active, false); EditorApplication.Exit(1);
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup - started > 600)
            {
                OnLog("Visibility resource capture timed out", "", LogType.Error); return;
            }
            if (!EditorApplication.isPlaying) return;
            var sample = UnityEngine.Object.FindFirstObjectByType<VisibilityAssetSample>();
            if (sample == null || !sample.Complete) return;
            SessionState.SetBool(Active, false);
            Debug.Log(sample.Failure == null ? "D12_VISIBILITY_CAPTURE_PASS" : sample.Failure);
            EditorApplication.Exit(sample.Failure == null ? 0 : 1);
        }
    }
}
