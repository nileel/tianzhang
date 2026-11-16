using System;
using System.IO;
using TianZhang.Art.Jindan.Fire.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Art.Jindan.Fire.Editor
{
    public static class FireAssetCapture
    {
        private const string Active = "D12FireCaptureActive";
        private static double started;

        public static void Capture1920() => Begin(1920);
        public static void Capture1366() => Begin(1366);

        private static void Begin(int width)
        {
            EditorSceneManager.OpenScene(FireAssetBuilder.Root + "/Samples/FireAssetSample.unity");
            var sample = UnityEngine.Object.FindFirstObjectByType<FireAssetSample>();
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
            File.WriteAllText(Path.Combine(FireAssetBuilder.Evidence, "capture-error.txt"), condition + "\n" + stackTrace);
            SessionState.SetBool(Active, false); EditorApplication.Exit(1);
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup - started > 600)
            {
                OnLog("Fire resource capture timed out", "", LogType.Error); return;
            }
            if (!EditorApplication.isPlaying) return;
            var sample = UnityEngine.Object.FindFirstObjectByType<FireAssetSample>();
            if (sample == null || !sample.Complete) return;
            SessionState.SetBool(Active, false);
            Debug.Log(sample.Failure == null ? "D12_FIRE_CAPTURE_PASS" : sample.Failure);
            EditorApplication.Exit(sample.Failure == null ? 0 : 1);
        }
    }
}
