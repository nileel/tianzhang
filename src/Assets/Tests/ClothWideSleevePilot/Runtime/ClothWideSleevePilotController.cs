using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TianZhang.ClothWideSleevePilot
{
    public sealed class ClothWideSleevePilotController : MonoBehaviour
    {
        public const float TotalDurationSeconds = 72f;
        public const float TacticalOrthographicSize = 6.2f;
        private const int SimulationRate = 60;
        private const int CaptureStride = 5;
        private static readonly float[] SixDirectionYaw = { 90f, 150f, 210f, 270f, 330f, 30f };

        [SerializeField] private Animator animator;
        [SerializeField] private Transform motionRoot;
        [SerializeField] private Camera experimentCamera;
        [SerializeField] private Cloth sleeveCloth;
        [SerializeField] private SkinnedMeshRenderer sleeveRenderer;

        private HumanPoseHandler poseHandler;
        private HumanPose pose;
        private float[] baseMuscles;
        private Vector3 originPosition, cameraOriginPosition, previousPosition;
        private Quaternion originRotation, previousRotation;
        private int armUpIndex, armForwardIndex, forearmIndex;
        private int simulationFrame, frameWithinTrial, direction = -1;
        private float playbackTime, elapsed, trialTime, wallStart;
        private float previousCaptureDelta;
        private int previousFrameRate, previousVSync;
        private bool running, finished, initialized;
        private string captureDirectory, actionLabel = "WARMUP";
        private Camera tacticalCamera, debugCamera;
        private ClothWideSleeveCollisionProbe probe;
        private readonly List<FrameRecord> frames = new List<FrameRecord>();
        private readonly List<StepRecord> steps = new List<StepRecord>();
        private GUIStyle titleStyle, detailStyle;

        public Animator Animator => animator;
        public Camera ExperimentCamera => experimentCamera;
        public Cloth SleeveCloth => sleeveCloth;
        public SkinnedMeshRenderer SleeveRenderer => sleeveRenderer;
        private bool Capturing => !string.IsNullOrWhiteSpace(captureDirectory);

        private IEnumerator Start()
        {
            if (animator == null || motionRoot == null || experimentCamera == null || sleeveCloth == null ||
                sleeveRenderer == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The isolated Cloth pilot has invalid serialized references.");

            captureDirectory = ReadCommandLineValue("--capture-dir");
            previousCaptureDelta = Time.captureDeltaTime;
            previousFrameRate = Application.targetFrameRate;
            previousVSync = QualitySettings.vSyncCount;
            initialized = true;
            Application.targetFrameRate = SimulationRate;
            if (Capturing)
            {
                captureDirectory = Path.GetFullPath(captureDirectory);
                Directory.CreateDirectory(captureDirectory);
                Time.captureFramerate = SimulationRate;
                QualitySettings.vSyncCount = 0;
                Screen.SetResolution(1280, 720, false);
            }

            originPosition = motionRoot.position;
            originRotation = motionRoot.rotation;
            cameraOriginPosition = experimentCamera.transform.position;
            poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            poseHandler.GetHumanPose(ref pose);
            baseMuscles = (float[])pose.muscles.Clone();
            armUpIndex = RequireMuscle("Left Arm Down-Up");
            armForwardIndex = RequireMuscle("Left Arm Front-Back");
            forearmIndex = RequireMuscle("Left Forearm Stretch");
            ConfigureViews();
            probe = new ClothWideSleeveCollisionProbe(sleeveCloth, sleeveRenderer);

            sleeveCloth.enabled = false;
            ApplyPose(0, 0f);
            // Exclude shader/window startup from the recorded trial; Cloth is still disabled.
            for (int i = 0; i < SimulationRate; i++) yield return new WaitForEndOfFrame();
            wallStart = Time.realtimeSinceStartup;
            running = true;
            StartCoroutine(CaptureFrames());
        }

        private void Update()
        {
            if (!running) return;
            elapsed = Capturing ? simulationFrame / (float)SimulationRate : playbackTime;
            if (elapsed >= TotalDurationSeconds)
            {
                running = false;
                finished = true;
                if (Capturing)
                {
                    WriteReport();
                    Application.Quit();
                }
                return;
            }

            int nextDirection = Mathf.FloorToInt(elapsed / ClothWideSleeveMotion.TrialSeconds);
            trialTime = elapsed - nextDirection * ClothWideSleeveMotion.TrialSeconds;
            bool reset = nextDirection != direction;
            if (reset)
            {
                sleeveCloth.enabled = false;
                direction = nextDirection;
                frameWithinTrial = 0;
            }
            ApplyPose(direction, trialTime);
            if (frameWithinTrial == 1)
            {
                sleeveCloth.enabled = true;
                sleeveCloth.ClearTransformMotion();
            }
            if (Capturing)
                steps.Add(new StepRecord
                {
                    simulationFrame = simulationFrame, time = elapsed, trialTime = trialTime,
                    direction = direction, reset = reset, rootPosition = motionRoot.position,
                    rootStepMeters = reset ? 0f : Vector3.Distance(previousPosition, motionRoot.position),
                    rootRotationStepDegrees = reset ? 0f : Quaternion.Angle(previousRotation, motionRoot.rotation)
                });
            previousPosition = motionRoot.position;
            previousRotation = motionRoot.rotation;
            playbackTime += Time.deltaTime;
            frameWithinTrial++;
        }

        private void LateUpdate()
        {
            if (probe != null) probe.UpdateLines();
        }

        private IEnumerator CaptureFrames()
        {
            while (running)
            {
                yield return new WaitForEndOfFrame();
                // Start can resume at EndOfFrame: do not capture until the first actual Update.
                if (!running || direction < 0) continue;
                if (Capturing && simulationFrame % CaptureStride == 0)
                {
                    var record = new FrameRecord
                    {
                        captureIndex = frames.Count, simulationFrame = simulationFrame, time = elapsed,
                        trialTime = trialTime, wallSeconds = Time.realtimeSinceStartup - wallStart,
                        direction = direction, yaw = SixDirectionYaw[direction], action = actionLabel,
                        clothEnabled = sleeveCloth.enabled, rootPosition = motionRoot.position,
                        rootRotation = motionRoot.rotation,
                        measurement = sleeveCloth.enabled ? probe.Measure() : null
                    };
                    Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(captureDirectory, "frame_" + frames.Count.ToString("D4") + ".png"),
                        texture.EncodeToPNG());
                    Destroy(texture);
                    frames.Add(record);
                }
                simulationFrame++;
            }
        }

        private void ApplyPose(int facingIndex, float time)
        {
            ClothWideSleeveMotion.Sample sample = ClothWideSleeveMotion.Evaluate(time);
            Array.Copy(baseMuscles, pose.muscles, baseMuscles.Length);
            pose.muscles[armUpIndex] = sample.armUp;
            pose.muscles[armForwardIndex] = sample.armForward;
            pose.muscles[forearmIndex] = sample.forearm;
            poseHandler.SetHumanPose(ref pose);
            Quaternion facing = originRotation * Quaternion.Euler(0f, SixDirectionYaw[facingIndex], 0f);
            motionRoot.position = originPosition + facing * Vector3.forward * sample.distance;
            motionRoot.rotation = facing * Quaternion.Euler(0f, sample.yawOffset, 0f);
            actionLabel = sample.action;
            Vector3 cameraPosition = cameraOriginPosition + motionRoot.position - originPosition;
            experimentCamera.transform.position = cameraPosition;
            tacticalCamera.transform.position = cameraPosition;
            debugCamera.transform.position = cameraPosition;
        }

        private void ConfigureViews()
        {
            experimentCamera.rect = new Rect(0f, 0f, 0.5f, 1f);
            experimentCamera.orthographicSize = 1.5f;
            tacticalCamera = AddView("Tactical Scale 6.2", new Rect(0.5f, 0f, 0.5f, 0.5f), 6.2f, 1f);
            debugCamera = AddView("Collider X-Ray Near View", new Rect(0.5f, 0.5f, 0.5f, 0.5f), 1.5f, 2f);
        }

        private Camera AddView(string objectName, Rect viewport, float size, float depth)
        {
            var go = new GameObject(objectName, typeof(Camera));
            go.transform.SetParent(transform, false);
            var camera = go.GetComponent<Camera>();
            camera.CopyFrom(experimentCamera);
            camera.transform.SetPositionAndRotation(experimentCamera.transform.position, experimentCamera.transform.rotation);
            camera.rect = viewport;
            camera.orthographicSize = size;
            camera.depth = depth;
            return camera;
        }

        private void OnGUI()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                titleStyle.normal.textColor = Color.white;
                detailStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                detailStyle.normal.textColor = Color.white;
            }
            if (probe != null && debugCamera != null) probe.DrawOverlay(debugCamera);
            GUI.Box(new Rect(8, 8, Screen.width * 0.5f - 16, 98), GUIContent.none);
            GUI.Label(new Rect(18, 12, 590, 28), "WIDE SLEEVE / RETEST 02 / UNITY CLOTH", titleStyle);
            GUI.Label(new Rect(18, 42, 590, 25), "Near | t=" + elapsed.ToString("F2") + "s | frame=" + simulationFrame +
                " | yaw=" + (direction < 0 ? "warmup" : SixDirectionYaw[direction].ToString("F0")), detailStyle);
            GUI.Label(new Rect(18, 70, 590, 25), finished ? "COMPLETE - re-enter Play to replay" : actionLabel, detailStyle);
            GUI.Box(new Rect(Screen.width * 0.5f + 8, 8, Screen.width * 0.5f - 16, 55), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f + 18, 12, 620, 24), "X-RAY PROXIES (overlay, not extra colliders)", detailStyle);
            GUI.Label(new Rect(Screen.width * 0.5f + 18, 35, 620, 24), "Yellow: torso | Cyan: upper arm | Magenta: forearm", detailStyle);
            GUI.Box(new Rect(Screen.width * 0.5f + 8, Screen.height * 0.5f + 8, Screen.width * 0.5f - 16, 34), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f + 18, Screen.height * 0.5f + 12, 600, 26), "TACTICAL: ortho 6.2 / fixed oblique camera", detailStyle);
        }

        private void WriteReport()
        {
            var collisionIndices = new List<uint>();
            sleeveCloth.GetSelfAndInterCollisionIndices(collisionIndices);
            int pins = 0;
            float maxDistance = 0f;
            foreach (ClothSkinningCoefficient coefficient in sleeveCloth.coefficients)
            {
                if (coefficient.maxDistance <= 0.0001f) pins++;
                maxDistance = Mathf.Max(maxDistance, coefficient.maxDistance);
            }
            var report = new RuntimeReport
            {
                unityVersion = Application.unityVersion, clothObject = sleeveCloth.name,
                simulationHz = SimulationRate, captureHz = SimulationRate / CaptureStride,
                durationSeconds = TotalDurationSeconds, capturedFrames = frames.Count,
                wallSecondsIncludingCapture = Time.realtimeSinceStartup - wallStart,
                vertexCount = sleeveRenderer.sharedMesh.vertexCount, pinnedVertices = pins,
                bendingStiffness = sleeveCloth.bendingStiffness, stretchingStiffness = sleeveCloth.stretchingStiffness,
                maximumDistance = maxDistance, colliderPairs = sleeveCloth.sphereColliders.Length,
                selfCollisionVertices = collisionIndices.Count, tacticalOrthographicSize = TacticalOrthographicSize,
                rendererLossyScale = sleeveRenderer.transform.lossyScale,
                cameraEuler = experimentCamera.transform.eulerAngles, testedDirectionYaw = SixDirectionYaw,
                frames = frames.ToArray(), steps = steps.ToArray()
            };
            File.WriteAllText(Path.Combine(captureDirectory, "runtime-report.json"), JsonUtility.ToJson(report, true));
        }

        private int RequireMuscle(string muscleName)
        {
            int index = Array.IndexOf(HumanTrait.MuscleName, muscleName);
            if (index < 0) throw new InvalidOperationException("Missing humanoid muscle: " + muscleName);
            return index;
        }

        private static string ReadCommandLineValue(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return null;
        }

        private void OnDestroy()
        {
            if (poseHandler != null) poseHandler.Dispose();
            if (probe != null) probe.Dispose();
            if (!initialized) return;
            Time.captureDeltaTime = previousCaptureDelta;
            Application.targetFrameRate = previousFrameRate;
            QualitySettings.vSyncCount = previousVSync;
        }

        [Serializable]
        private sealed class StepRecord
        {
            public int simulationFrame, direction;
            public float time, trialTime, rootStepMeters, rootRotationStepDegrees;
            public bool reset;
            public Vector3 rootPosition;
        }

        [Serializable]
        private sealed class FrameRecord
        {
            public int captureIndex, simulationFrame, direction;
            public float time, trialTime, wallSeconds, yaw;
            public string action;
            public bool clothEnabled;
            public Vector3 rootPosition;
            public Quaternion rootRotation;
            public ClothWideSleeveCollisionProbe.Measurement measurement;
        }

        [Serializable]
        private sealed class RuntimeReport
        {
            public string unityVersion, clothObject;
            public int simulationHz, captureHz, capturedFrames, vertexCount, pinnedVertices, colliderPairs, selfCollisionVertices;
            public float durationSeconds, wallSecondsIncludingCapture, bendingStiffness, stretchingStiffness,
                maximumDistance, tacticalOrthographicSize;
            public Vector3 rendererLossyScale, cameraEuler;
            public float[] testedDirectionYaw;
            public FrameRecord[] frames;
            public StepRecord[] steps;
        }
    }
}
