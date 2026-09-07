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
        public enum IsolationMode { Retest02, A_SkinOnly, B_NoBodyCollision, C_BodyCollision }
        // Authored constraint-mask experiment for the frozen v001 FBX only. Evidence: isolation-03/analysis.json.
        private static readonly int[] DiagnosedTorsoConflictVertices =
        {
            360, 361, 362, 363, 364, 365, 367, 369, 400, 401, 402, 403, 404, 405,
            406, 407, 408, 409, 410, 411, 412, 413, 445, 447, 449, 451, 453, 455
        };

        [SerializeField] private Animator animator;
        [SerializeField] private Transform motionRoot;
        [SerializeField] private Camera experimentCamera;
        [SerializeField] private Cloth sleeveCloth;
        [SerializeField] private SkinnedMeshRenderer sleeveRenderer;
        [SerializeField] private IsolationMode isolationMode;
        [SerializeField] private bool releaseDiagnosedTorsoPins;
        [SerializeField] private bool correctTorsoTransition;
        [SerializeField] private bool expandSleeveRange;
        [SerializeField] private bool initializeFromOpenPose;

        private HumanPoseHandler poseHandler;
        private HumanPose pose;
        private float[] baseMuscles;
        private ClothWideSleeveMotion.Sample openPose;
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
        private ClothWideSleeveSkinningProbe skinningProbe;
        private ClothWideSleeveTorsoCorrection.SleeveRangeProof sleeveRangeProof;
        private readonly List<FrameRecord> frames = new List<FrameRecord>();
        private readonly List<StepRecord> steps = new List<StepRecord>();
        private GUIStyle titleStyle, detailStyle;

        public Animator Animator => animator;
        public Camera ExperimentCamera => experimentCamera;
        public Cloth SleeveCloth => sleeveCloth;
        public SkinnedMeshRenderer SleeveRenderer => sleeveRenderer;
        private bool Capturing => !string.IsNullOrWhiteSpace(captureDirectory);
        private bool Isolating => isolationMode != IsolationMode.Retest02;
        private float TrialDuration => ClothWideSleeveMotion.TrialSeconds +
            (initializeFromOpenPose ? ClothWideSleeveMotion.InitializationSeconds : 0f);
        private float Duration => Isolating ? 8f : SixDirectionYaw.Length * TrialDuration;

        private IEnumerator Start()
        {
            if (animator == null || motionRoot == null || experimentCamera == null || sleeveCloth == null ||
                sleeveRenderer == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The isolated Cloth pilot has invalid serialized references.");

            captureDirectory = ReadCommandLineValue("--capture-dir");
            string isolationArgument = ReadCommandLineValue("--isolate-mode");
            if (isolationArgument != null) isolationMode = ParseIsolationMode(isolationArgument);
            if (ReadCommandLineValue("--release-conflicting-pins") == "true") releaseDiagnosedTorsoPins = true;
            if (ReadCommandLineValue("--correct-torso-transition") == "true") correctTorsoTransition = true;
            if (ReadCommandLineValue("--expand-sleeve-range") == "true") expandSleeveRange = true;
            if (ReadCommandLineValue("--initialize-from-open-pose") == "true") initializeFromOpenPose = true;
            if (initializeFromOpenPose && !expandSleeveRange)
                throw new InvalidOperationException("Initialization 07 requires the unchanged Sleeve Range 06 inputs.");
            if (expandSleeveRange && (!correctTorsoTransition || Isolating))
                throw new InvalidOperationException("Sleeve Range 06 requires Torso Transition 05 and the full motion trial.");
            if (correctTorsoTransition && releaseDiagnosedTorsoPins)
                throw new InvalidOperationException("Select the torso candidate OR the old pin-release baseline, not both.");
            if (releaseDiagnosedTorsoPins)
            {
                var coefficients = sleeveCloth.coefficients;
                ReleaseDiagnosedPins(coefficients);
                sleeveCloth.coefficients = coefficients;
            }
            if (isolationMode == IsolationMode.B_NoBodyCollision)
            {
                sleeveCloth.sphereColliders = Array.Empty<ClothSphereColliderPair>();
                sleeveCloth.capsuleColliders = Array.Empty<CapsuleCollider>();
            }
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
            openPose = new ClothWideSleeveMotion.Sample
            {
                armUp = baseMuscles[armUpIndex], armForward = baseMuscles[armForwardIndex],
                forearm = baseMuscles[forearmIndex]
            };
            ConfigureViews();

            sleeveCloth.enabled = false;
            ApplyPose(Isolating ? 1 : 0, 0f);
            if (correctTorsoTransition) ClothWideSleeveTorsoCorrection.Apply(sleeveCloth, animator.transform);
            if (expandSleeveRange) sleeveRangeProof =
                ClothWideSleeveTorsoCorrection.ExpandSleeveRange(sleeveCloth, sleeveRenderer, animator.transform);
            // 05 must be prepared in its original lowered pose before changing the startup pose.
            if (initializeFromOpenPose) ApplyPose(0, 0f, true);
            probe = new ClothWideSleeveCollisionProbe(sleeveCloth, sleeveRenderer);
            if (Isolating || correctTorsoTransition) skinningProbe = new ClothWideSleeveSkinningProbe(sleeveRenderer, sleeveCloth,
                ReadCommandLineValue("--body-probe") == "true" ? animator : null);
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
            if (elapsed >= Duration)
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

            int nextDirection = Isolating ? 1 : Mathf.FloorToInt(elapsed / TrialDuration);
            trialTime = Isolating ? elapsed : elapsed - nextDirection * TrialDuration;
            bool reset = nextDirection != direction;
            if (reset)
            {
                sleeveCloth.enabled = false;
                direction = nextDirection;
                frameWithinTrial = 0;
            }
            ApplyPose(direction, trialTime, initializeFromOpenPose);
            if (frameWithinTrial == 1 && isolationMode != IsolationMode.A_SkinOnly)
            {
                sleeveCloth.enabled = true;
                sleeveCloth.ClearTransformMotion();
            }
            if (Capturing)
                steps.Add(new StepRecord
                {
                    simulationFrame = simulationFrame, time = elapsed, trialTime = trialTime,
                    direction = direction, reset = reset, rootPosition = motionRoot.position,
                    clothEnabled = sleeveCloth.enabled, armUp = pose.muscles[armUpIndex],
                    armForward = pose.muscles[armForwardIndex], forearm = pose.muscles[forearmIndex],
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
                    int trialFrame = Mathf.RoundToInt((trialTime -
                        (initializeFromOpenPose ? ClothWideSleeveMotion.InitializationSeconds : 0f)) * SimulationRate);
                    var record = new FrameRecord
                    {
                        captureIndex = frames.Count, simulationFrame = simulationFrame, time = elapsed,
                        trialTime = trialTime, wallSeconds = Time.realtimeSinceStartup - wallStart,
                        direction = direction, yaw = SixDirectionYaw[direction], action = actionLabel,
                        clothEnabled = sleeveCloth.enabled, rootPosition = motionRoot.position,
                        rootRotation = motionRoot.rotation,
                        measurement = !Isolating && sleeveCloth.enabled ? probe.Measure() : null,
                        skinning = skinningProbe != null && (Isolating || direction == 1) ?
                            skinningProbe.Measure(trialFrame % 60 == 0 ||
                            trialFrame == 295 || trialFrame == 330 || trialFrame == 475) : null
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

        private void ApplyPose(int facingIndex, float time, bool includeInitialization = false)
        {
            ClothWideSleeveMotion.Sample sample = includeInitialization ?
                ClothWideSleeveMotion.EvaluateWithInitialization(time, openPose) : EvaluateMotion(time, Isolating);
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
            GUI.Label(new Rect(18, 12, 590, 28), initializeFromOpenPose ? "INITIALIZATION 07 / CANDIDATE" :
                expandSleeveRange ? "SLEEVE RANGE 06 / CANDIDATE" :
                correctTorsoTransition ? "TORSO TRANSITION 05 / CANDIDATE" :
                releaseDiagnosedTorsoPins ? "PIN RELEASE 01 / 28 TARGETED VERTICES" :
                Isolating ? "ISOLATION 03 / " + isolationMode :
                "WIDE SLEEVE / RETEST 02 / UNITY CLOTH", titleStyle);
            GUI.Label(new Rect(18, 42, 590, 25), "Near | t=" + elapsed.ToString("F2") + "s | frame=" + simulationFrame +
                " | yaw=" + (direction < 0 ? "warmup" : SixDirectionYaw[direction].ToString("F0")), detailStyle);
            GUI.Label(new Rect(18, 70, 590, 25), finished ? "COMPLETE - re-enter Play to replay" : actionLabel, detailStyle);
            GUI.Box(new Rect(Screen.width * 0.5f + 8, 8, Screen.width * 0.5f - 16, 55), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f + 18, 12, 620, 24), "PROXIES: " + sleeveCloth.sphereColliders.Length +
                " pairs | CLOTH " + (sleeveCloth.enabled ? "ON" : "OFF / skin only"), detailStyle);
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
                unityVersion = Application.unityVersion, clothObject = sleeveCloth.name, isolationMode = isolationMode.ToString(),
                releaseDiagnosedTorsoPins = releaseDiagnosedTorsoPins,
                correctTorsoTransition = correctTorsoTransition,
                expandSleeveRange = expandSleeveRange, sleeveRangeProof = sleeveRangeProof,
                initializeFromOpenPose = initializeFromOpenPose,
                initializationSeconds = initializeFromOpenPose ? ClothWideSleeveMotion.InitializationSeconds : 0f,
                effectiveMaxDistances = Array.ConvertAll(sleeveCloth.coefficients, c => c.maxDistance),
                simulationHz = SimulationRate, captureHz = SimulationRate / CaptureStride,
                durationSeconds = Duration, capturedFrames = frames.Count,
                wallSecondsIncludingCapture = Time.realtimeSinceStartup - wallStart,
                vertexCount = sleeveRenderer.sharedMesh.vertexCount, pinnedVertices = pins,
                bendingStiffness = sleeveCloth.bendingStiffness, stretchingStiffness = sleeveCloth.stretchingStiffness,
                maximumDistance = maxDistance, colliderPairs = sleeveCloth.sphereColliders.Length,
                selfCollisionVertices = collisionIndices.Count, tacticalOrthographicSize = TacticalOrthographicSize,
                rendererLossyScale = sleeveRenderer.transform.lossyScale,
                cameraEuler = experimentCamera.transform.eulerAngles,
                testedDirectionYaw = Isolating ? new[] { SixDirectionYaw[1] } : SixDirectionYaw,
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

        public static IsolationMode ParseIsolationMode(string value)
        {
            switch (value)
            {
                case "A": return IsolationMode.A_SkinOnly;
                case "B": return IsolationMode.B_NoBodyCollision;
                case "C": return IsolationMode.C_BodyCollision;
                default: throw new ArgumentException("--isolate-mode must be A, B or C.");
            }
        }

        public static void ReleaseDiagnosedPins(ClothSkinningCoefficient[] coefficients)
        {
            if (coefficients.Length != 456) throw new InvalidOperationException("Pin mask requires the frozen 456-vertex sleeve.");
            float existingMaximum = 0f;
            foreach (var coefficient in coefficients) existingMaximum = Mathf.Max(existingMaximum, coefficient.maxDistance);
            foreach (int index in DiagnosedTorsoConflictVertices)
                if (coefficients[index].maxDistance > 0.0001f)
                    throw new InvalidOperationException("Diagnosed pin mask no longer matches this asset.");
            foreach (int index in DiagnosedTorsoConflictVertices) coefficients[index].maxDistance = existingMaximum;
        }

        public static ClothWideSleeveMotion.Sample EvaluateMotion(float time, bool isolate)
        {
            var sample = ClothWideSleeveMotion.Evaluate(isolate && time >= 6f ? 0f : time);
            if (isolate && time >= 6f) sample.action = "RECOVER AT REST";
            return sample;
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
            if (skinningProbe != null) skinningProbe.Dispose();
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
            public float armUp, armForward, forearm;
            public bool reset, clothEnabled;
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
            public ClothWideSleeveSkinningProbe.Snapshot skinning;
        }

        [Serializable]
        private sealed class RuntimeReport
        {
            public string unityVersion, clothObject, isolationMode;
            public bool releaseDiagnosedTorsoPins, correctTorsoTransition, expandSleeveRange;
            public bool initializeFromOpenPose;
            public float initializationSeconds;
            public ClothWideSleeveTorsoCorrection.SleeveRangeProof sleeveRangeProof;
            public int simulationHz, captureHz, capturedFrames, vertexCount, pinnedVertices, colliderPairs, selfCollisionVertices;
            public float durationSeconds, wallSecondsIncludingCapture, bendingStiffness, stretchingStiffness,
                maximumDistance, tacticalOrthographicSize;
            public Vector3 rendererLossyScale, cameraEuler;
            public float[] testedDirectionYaw;
            public float[] effectiveMaxDistances;
            public FrameRecord[] frames;
            public StepRecord[] steps;
        }
    }
}
