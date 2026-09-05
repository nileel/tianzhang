using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TianZhang.ClothWideSleevePilot
{
    public sealed class ClothWideSleevePilotController : MonoBehaviour
    {
        public const float TotalDurationSeconds = 32f;
        public const float TacticalOrthographicSize = 6.2f;

        private static readonly float[] SixDirectionYaw = { 90f, 150f, 210f, 270f, 330f, 30f };

        [SerializeField] private Animator animator;
        [SerializeField] private Transform motionRoot;
        [SerializeField] private Camera experimentCamera;
        [SerializeField] private Cloth sleeveCloth;
        [SerializeField] private SkinnedMeshRenderer sleeveRenderer;

        private HumanPoseHandler poseHandler;
        private HumanPose pose;
        private float[] baseMuscles;
        private Vector3 originPosition;
        private Quaternion originRotation;
        private Vector3 cameraOriginPosition;
        private float startTime;
        private float nextCaptureTime;
        private string captureDirectory;
        private int captureFrameCount;
        private bool captureFinishing;
        private float accumulatedFrameTime;
        private float maximumFrameTime;
        private int measuredFrameCount;
        private string phaseLabel = "INITIAL SETTLE";
        private string actionLabel = "Cloth settling on the posed character";
        private GUIStyle titleStyle;
        private GUIStyle detailStyle;

        private int armDownUpIndex;
        private int armFrontBackIndex;
        private int armTwistIndex;
        private int forearmStretchIndex;
        private int forearmTwistIndex;

        public Animator Animator => animator;
        public Camera ExperimentCamera => experimentCamera;
        public Cloth SleeveCloth => sleeveCloth;
        public SkinnedMeshRenderer SleeveRenderer => sleeveRenderer;

        private void Start()
        {
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            Application.targetFrameRate = 60;
            originPosition = motionRoot.position;
            originRotation = motionRoot.rotation;
            cameraOriginPosition = experimentCamera.transform.position;
            poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            poseHandler.GetHumanPose(ref pose);
            baseMuscles = (float[])pose.muscles.Clone();

            armDownUpIndex = RequireMuscle("Left Arm Down-Up");
            armFrontBackIndex = RequireMuscle("Left Arm Front-Back");
            armTwistIndex = RequireMuscle("Left Arm Twist In-Out");
            forearmStretchIndex = RequireMuscle("Left Forearm Stretch");
            forearmTwistIndex = RequireMuscle("Left Forearm Twist In-Out");

            captureDirectory = ReadCommandLineValue("--capture-dir");
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                captureDirectory = Path.GetFullPath(captureDirectory);
                Directory.CreateDirectory(captureDirectory);
                QualitySettings.vSyncCount = 0;
                Screen.SetResolution(960, 540, false);
            }

            sleeveCloth.enabled = false;
            ApplyPose(0f);
            sleeveCloth.ClearTransformMotion();
            startTime = Time.unscaledTime;
            nextCaptureTime = startTime;
            StartCoroutine(EnableClothAfterInitialPose());
        }

        private IEnumerator EnableClothAfterInitialPose()
        {
            yield return new WaitForFixedUpdate();
            sleeveCloth.enabled = true;
            sleeveCloth.ClearTransformMotion();
        }

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            accumulatedFrameTime += delta;
            maximumFrameTime = Mathf.Max(maximumFrameTime, delta);
            measuredFrameCount++;

            float elapsed = Time.unscaledTime - startTime;
            ApplyPose(Mathf.Min(elapsed, TotalDurationSeconds));

            if (!string.IsNullOrWhiteSpace(captureDirectory) && !captureFinishing && elapsed >= TotalDurationSeconds)
            {
                captureFinishing = true;
                StartCoroutine(FinishCapture());
            }
        }

        private void LateUpdate()
        {
            if (string.IsNullOrWhiteSpace(captureDirectory) || captureFinishing)
                return;

            float now = Time.unscaledTime;
            if (now + 0.0001f < nextCaptureTime)
                return;

            string fileName = "frame_" + captureFrameCount.ToString("D4") + ".png";
            ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, fileName), 1);
            captureFrameCount++;
            nextCaptureTime += 0.125f;
        }

        private IEnumerator FinishCapture()
        {
            phaseLabel = "CAPTURE COMPLETE";
            actionLabel = "Writing the single-character runtime report";
            yield return new WaitForSecondsRealtime(1.25f);
            WriteRuntimeReport();
            Application.Quit(0);
        }

        private void ApplyPose(float elapsed)
        {
            Array.Copy(baseMuscles, pose.muscles, baseMuscles.Length);
            float armDownUp = -0.62f;
            float armFrontBack = 0f;
            float armTwist = 0f;
            float forearmStretch = 0.08f;
            float forearmTwist = 0f;
            Vector3 rootPosition = originPosition;
            Quaternion rootRotation = originRotation;

            if (elapsed < 2f)
            {
                experimentCamera.orthographicSize = 1.35f;
                phaseLabel = "INITIAL SETTLE · NEAR";
                actionLabel = "Pinned sleeve root and free sleeve belly settling";
            }
            else if (elapsed < 10f)
            {
                experimentCamera.orthographicSize = 1.35f;
                phaseLabel = "NEAR VIEW";
                ApplyFourActionCycle(elapsed - 2f, ref armDownUp, ref armFrontBack, ref forearmStretch,
                    ref rootPosition, ref rootRotation);
            }
            else if (elapsed < 18f)
            {
                experimentCamera.orthographicSize = TacticalOrthographicSize;
                phaseLabel = "TIANZHANG TACTICAL SCALE · ORTHO 6.2";
                ApplyFourActionCycle(elapsed - 10f, ref armDownUp, ref armFrontBack, ref forearmStretch,
                    ref rootPosition, ref rootRotation);
            }
            else if (elapsed < 30f)
            {
                experimentCamera.orthographicSize = 2.25f;
                float directionTime = elapsed - 18f;
                int direction = Mathf.Min(5, Mathf.FloorToInt(directionTime / 2f));
                float directionPhase = directionTime - direction * 2f;
                rootRotation = originRotation * Quaternion.Euler(0f, SixDirectionYaw[direction], 0f);
                armDownUp = Mathf.Lerp(-0.62f, 0.62f, Mathf.Sin(directionPhase * Mathf.PI * 0.5f));
                armFrontBack = Mathf.Sin(directionPhase * Mathf.PI * 2f) * 0.38f;
                forearmStretch = Mathf.Lerp(0.1f, -0.35f, Mathf.Sin(directionPhase * Mathf.PI));
                phaseLabel = "FIXED OBLIQUE SIX-DIRECTION CHECK";
                actionLabel = "Facing " + direction + " · yaw " + SixDirectionYaw[direction].ToString("0") + "°";
            }
            else
            {
                experimentCamera.orthographicSize = 1.35f;
                rootRotation = originRotation * Quaternion.Euler(0f, 30f, 0f);
                phaseLabel = "FINAL NEAR SETTLE";
                actionLabel = "Observe sleeve-belly return, jitter and residual folding";
            }

            SetMuscle(armDownUpIndex, armDownUp);
            SetMuscle(armFrontBackIndex, armFrontBack);
            SetMuscle(armTwistIndex, armTwist);
            SetMuscle(forearmStretchIndex, forearmStretch);
            SetMuscle(forearmTwistIndex, forearmTwist);
            poseHandler.SetHumanPose(ref pose);
            motionRoot.SetPositionAndRotation(rootPosition, rootRotation);
            experimentCamera.transform.position = cameraOriginPosition + rootPosition - originPosition;
        }

        private void ApplyFourActionCycle(float cycleTime, ref float armDownUp, ref float armFrontBack,
            ref float forearmStretch, ref Vector3 rootPosition, ref Quaternion rootRotation)
        {
            if (cycleTime < 2f)
            {
                float triangle = 1f - Mathf.Abs(cycleTime - 1f);
                armDownUp = Mathf.Lerp(-0.62f, 0.72f, Mathf.SmoothStep(0f, 1f, triangle));
                forearmStretch = Mathf.Lerp(0.08f, -0.18f, triangle);
                actionLabel = "1/4 RAISE AND LOWER LEFT ARM";
                return;
            }

            if (cycleTime < 4f)
            {
                float swingTime = cycleTime - 2f;
                armDownUp = -0.18f + Mathf.Sin(swingTime * Mathf.PI * 2f) * 0.33f;
                armFrontBack = Mathf.Sin(swingTime * Mathf.PI * 3f) * 0.82f;
                forearmStretch = -0.18f + Mathf.Sin(swingTime * Mathf.PI * 2f) * 0.28f;
                actionLabel = "2/4 PRONOUNCED ARM SWING";
                return;
            }

            if (cycleTime < 6f)
            {
                float turn = Mathf.SmoothStep(0f, 1f, (cycleTime - 4f) * 0.5f);
                rootRotation = originRotation * Quaternion.Euler(0f, Mathf.Lerp(-45f, 55f, turn), 0f);
                actionLabel = "3/4 BODY TURN";
                return;
            }

            float stopTime = cycleTime - 6f;
            float distance = stopTime < 0.9f
                ? Mathf.SmoothStep(0f, 1f, stopTime / 0.9f) * 1.25f
                : 1.25f;
            rootPosition = originPosition + new Vector3(0f, 0f, distance - 0.55f);
            armFrontBack = stopTime < 0.9f ? Mathf.Sin(stopTime * Mathf.PI * 4f) * 0.48f : 0f;
            actionLabel = stopTime < 0.9f ? "4/4 MOVE" : "4/4 ABRUPT STOP · OBSERVE RETURN";
        }

        private void SetMuscle(int index, float value)
        {
            pose.muscles[index] = Mathf.Clamp(value, -1f, 1f);
        }

        private int RequireMuscle(string name)
        {
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal))
                    return i;
            }

            throw new InvalidOperationException("Required Humanoid muscle is unavailable: " + name);
        }

        private bool ValidateReferences()
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                Debug.LogError("[ClothWideSleevePilot] A valid Humanoid avatar is required.");
                return false;
            }

            if (motionRoot == null || experimentCamera == null || sleeveCloth == null || sleeveRenderer == null)
            {
                Debug.LogError("[ClothWideSleevePilot] Serialized experiment references are incomplete.");
                return false;
            }

            return true;
        }

        private static string ReadCommandLineValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1];

                string prefix = name + "=";
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(prefix.Length);
            }

            return null;
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            GUI.Box(new Rect(14f, 14f, Mathf.Min(700f, Screen.width - 28f), 92f), GUIContent.none);
            GUI.Label(new Rect(28f, 22f, Screen.width - 56f, 30f),
                "UNITY BUILT-IN CLOTH · ONE QUATERNIUS STANDARD BODY · ONE LEFT WIDE SLEEVE", titleStyle);
            GUI.Label(new Rect(28f, 54f, Screen.width - 56f, 24f), phaseLabel + "  |  " + actionLabel, detailStyle);
            float fps = accumulatedFrameTime > 0.001f ? measuredFrameCount / accumulatedFrameTime : 0f;
            GUI.Label(new Rect(28f, 78f, Screen.width - 56f, 22f),
                "Cloth vertices " + sleeveRenderer.sharedMesh.vertexCount + " · FPS " + fps.ToString("0") +
                " · Single-character feasibility only; not a multi-character benchmark", detailStyle);
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.94f, 0.90f, 0.75f) }
            };
            detailStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };
        }

        private void WriteRuntimeReport()
        {
            var selfCollisionIndices = new List<uint>();
            sleeveCloth.GetSelfAndInterCollisionIndices(selfCollisionIndices);
            ClothSkinningCoefficient[] coefficients = sleeveCloth.coefficients;
            int pinned = 0;
            float maximumDistance = 0f;
            foreach (ClothSkinningCoefficient coefficient in coefficients)
            {
                if (coefficient.maxDistance <= 0.0001f)
                    pinned++;
                maximumDistance = Mathf.Max(maximumDistance, coefficient.maxDistance);
            }

            var report = new RuntimeReport
            {
                unityVersion = Application.unityVersion,
                durationSeconds = TotalDurationSeconds,
                capturedFrames = captureFrameCount,
                averageFramesPerSecond = accumulatedFrameTime > 0.001f ? measuredFrameCount / accumulatedFrameTime : 0f,
                maximumFrameTimeMilliseconds = maximumFrameTime * 1000f,
                clothVertices = sleeveRenderer.sharedMesh.vertexCount,
                pinnedVertices = pinned,
                maximumMotionConstraintMeters = maximumDistance,
                selfCollisionVertices = selfCollisionIndices.Count,
                colliderPairs = sleeveCloth.sphereColliders.Length,
                tacticalOrthographicSize = TacticalOrthographicSize,
                testedDirectionYaw = (float[])SixDirectionYaw.Clone()
            };
            File.WriteAllText(Path.Combine(captureDirectory, "runtime-report.json"), JsonUtility.ToJson(report, true));
        }

        private void OnDestroy()
        {
            if (poseHandler != null)
                poseHandler.Dispose();
        }

        [Serializable]
        private sealed class RuntimeReport
        {
            public string unityVersion;
            public float durationSeconds;
            public int capturedFrames;
            public float averageFramesPerSecond;
            public float maximumFrameTimeMilliseconds;
            public int clothVertices;
            public int pinnedVertices;
            public float maximumMotionConstraintMeters;
            public int selfCollisionVertices;
            public int colliderPairs;
            public float tacticalOrthographicSize;
            public float[] testedDirectionYaw;
        }
    }
}
