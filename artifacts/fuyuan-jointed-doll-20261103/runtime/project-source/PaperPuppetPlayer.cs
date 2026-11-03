using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PaperPuppetPlayer : MonoBehaviour
{
    public GameObject modelPrefab;
    public RuntimeAnimatorController controller;
    public AnimationClip[] clips;
    public Material groundMaterial;

    static readonly string[] Names = { "walk", "cast", "attack" };
    static readonly string[] Labels = { "走路", "施法", "攻击" };
    static readonly float[] Yaws = { 90, 150, 210, 270, 330, 30 };
    Transform facingRoot;
    Animator animator;
    Camera viewCamera;
    Font font;
    Bounds motionBounds;
    int selected, facing = 2, view;
    float time;
    bool playing = true, capturing;
    string status, captureDirectory;
    GUIStyle titleStyle, buttonStyle, labelStyle;

    void Start()
    {
        try
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 18);
            var args = Environment.GetCommandLineArgs();
            int captureArg = Array.IndexOf(args, "-capture");
            capturing = captureArg >= 0;
            if (capturing)
            {
                if (captureArg + 1 >= args.Length) throw new ArgumentException("-capture requires an output directory.");
                captureDirectory = Path.GetFullPath(args[captureArg + 1]);
                Directory.CreateDirectory(captureDirectory);
            }
            CreateStage();
            if (modelPrefab == null || controller == null || clips == null || clips.Length != 3)
                throw new InvalidOperationException("尚未导入苻渊关节人偶与走路、施法、攻击三个动作。");

            facingRoot = new GameObject("六向朝向").transform;
            var model = Instantiate(modelPrefab, facingRoot);
            model.name = "苻渊关节人偶";
            animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.enabled = false;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.updateWhenOffscreen = true;
            MeasureMotionBounds(model);
            SetFacing(facing);
            Select(0);
            Debug.Log("PUPPET_READY model=" + modelPrefab.name + " bounds=" + motionBounds + " rootMotion=false");
            if (capturing) StartCoroutine(Capture());
        }
        catch (Exception error)
        {
            status = error.Message;
            Debug.LogException(error);
            if (capturing)
            {
                if (!string.IsNullOrEmpty(captureDirectory)) File.WriteAllText(Path.Combine(captureDirectory, "failed.txt"), error.ToString());
                Application.Quit(2);
            }
        }
    }

    void CreateStage()
    {
        viewCamera = new GameObject("预览相机").AddComponent<Camera>();
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = new Color(.79f, .78f, .73f);
        viewCamera.orthographic = true;
        viewCamera.nearClipPlane = .01f;
        viewCamera.farClipPlane = 100f;
        viewCamera.allowHDR = false;
        viewCamera.rect = new Rect(0, .17f, 1, .77f);
        var backing = new GameObject("界面底色").AddComponent<Camera>();
        backing.depth = -2;
        backing.clearFlags = CameraClearFlags.SolidColor;
        backing.backgroundColor = new Color(.10f, .13f, .16f);
        backing.cullingMask = 0;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.76f, .76f, .76f);
        var light = new GameObject("柔光").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = .85f;
        light.transform.rotation = Quaternion.Euler(35, -35, 0);
        light.shadows = LightShadows.Soft;
        light.shadowStrength = .75f;
        light.shadowBias = .015f;
        light.shadowNormalBias = .02f;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.shadowDistance = 15f;
        var ground = new GameObject("中性哑光地面");
        ground.transform.position = new Vector3(0, -.005f, 0);
        var mesh = new Mesh { name = "10米平地" };
        mesh.vertices = new[] { new Vector3(-5, 0, -5), new Vector3(-5, 0, 5), new Vector3(5, 0, 5), new Vector3(5, 0, -5) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        var surface = ground.AddComponent<MeshRenderer>();
        surface.sharedMaterial = groundMaterial;
        surface.shadowCastingMode = ShadowCastingMode.Off;
        surface.receiveShadows = true;
    }

    void MeasureMotionBounds(GameObject model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException("模型没有可见网格。");
        bool initialized = false;
        var baked = new Mesh();
        foreach (var name in Names)
        {
            for (int sample = 0; sample <= 12; sample++)
            {
                animator.Play(name, 0, sample / 12f);
                animator.Update(0);
                foreach (var renderer in renderers)
                {
                    Bounds bounds = renderer.bounds;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        skin.BakeMesh(baked);
                        var local = baked.bounds;
                        bounds = new Bounds(skin.transform.TransformPoint(local.center), Vector3.zero);
                        for (int corner = 0; corner < 8; corner++)
                            bounds.Encapsulate(skin.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                    }
                    if (!initialized) { motionBounds = bounds; initialized = true; }
                    else motionBounds.Encapsulate(bounds);
                }
            }
        }
        Destroy(baked);
        if (motionBounds.size.y < .01f) throw new InvalidOperationException("模型高度无效。");
        Debug.Log("PUPPET_BOUNDS sampled=13_per_clip min=" + motionBounds.min.ToString("F4") + " max=" + motionBounds.max.ToString("F4"));
        FrameCamera();
    }

    void FrameCamera()
    {
        var center = new Vector3(0, motionBounds.center.y, 0);
        float radius = Mathf.Sqrt(Mathf.Pow(Mathf.Abs(motionBounds.center.x) + motionBounds.extents.x, 2) +
                                  Mathf.Pow(Mathf.Abs(motionBounds.center.z) + motionBounds.extents.z, 2) +
                                  motionBounds.extents.y * motionBounds.extents.y);
        float aspect = Mathf.Max(.1f, Screen.width * viewCamera.rect.width / (Screen.height * viewCamera.rect.height));
        viewCamera.orthographicSize = radius * 1.12f / Mathf.Min(1f, aspect);
        Vector3 direction = view == 0 ? new Vector3(-1, .65f, -1).normalized : view == 1 ? Vector3.back : Vector3.right;
        viewCamera.transform.position = center + direction * Mathf.Max(5, radius * 4);
        viewCamera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
    }

    void SetFacing(int value)
    {
        facing = value;
        facingRoot.localRotation = Quaternion.Euler(0, Yaws[value], 0);
    }

    void Select(int value)
    {
        selected = value;
        time = 0;
        Sample(0);
    }

    void Sample(float seconds)
    {
        time = seconds;
        animator.Play(Names[selected], 0, seconds / Mathf.Max(.001f, clips[selected].length));
        animator.Update(0);
    }

    void Update()
    {
        if (status != null || animator == null || capturing) return;
        FrameCamera();
        if (playing) Sample((time + Time.unscaledDeltaTime) % Mathf.Max(.001f, clips[selected].length));
    }

    void OnGUI()
    {
        if (font != null) GUI.skin.font = font;
        titleStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 21, fontStyle = FontStyle.Bold };
        buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 16 };
        labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
        GUI.Label(new Rect(8, 8, Screen.width - 16, 30), "苻渊 · 关节人偶实验", titleStyle);
        if (status != null) { GUI.Label(new Rect(20, 65, Screen.width - 40, 90), status, labelStyle); return; }
        if (animator == null) return;
        GUI.enabled = !capturing;
        GUILayout.BeginArea(new Rect(12, Screen.height * .84f, Screen.width - 24, Screen.height * .16f - 8));
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        if (GUILayout.Button(playing ? "暂停" : "播放", buttonStyle, GUILayout.Width(72))) playing = !playing;
        if (GUILayout.Button("重播", buttonStyle, GUILayout.Width(72))) Select(selected);
        for (int i = 0; i < Labels.Length; i++)
            if (GUILayout.Button((selected == i ? "● " : "") + Labels[i], buttonStyle, GUILayout.Width(90))) Select(i);
        foreach (var option in new[] { "斜前视", "正视", "侧视" })
        {
            int index = option == "斜前视" ? 0 : option == "正视" ? 1 : 2;
            if (GUILayout.Button((view == index ? "● " : "") + option, buttonStyle, GUILayout.Width(90))) { view = index; FrameCamera(); }
        }
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        GUILayout.Label("六向", labelStyle, GUILayout.Width(46));
        for (int i = 0; i < 6; i++)
            if (GUILayout.Button((facing == i ? "● " : "") + i, buttonStyle, GUILayout.Width(56))) SetFacing(i);
        GUILayout.Label(time.ToString("F2") + " / " + clips[selected].length.ToString("F2") + " 秒", labelStyle, GUILayout.Width(160));
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.EndArea();
        GUI.enabled = true;
    }

    IEnumerator Capture()
    {
        const int fps = 30;
        var segments = new List<CaptureSegment>();
        var hipsSamples = new List<HipsSample>();
        Transform hips = null;
        foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            if (bone.name == "hips") hips = bone;
        Debug.Log("PUPPET_HIPS_PROBE found=" + (hips != null) + " applyRootMotion=" + animator.applyRootMotion);
        int outputFrame = 0;
        Directory.CreateDirectory(Path.Combine(captureDirectory, "sequence"));
        Directory.CreateDirectory(Path.Combine(captureDirectory, "keyframes"));
        view = 0; FrameCamera();
        for (int action = 0; action < 3; action++)
        {
            Select(action);
            int count = Mathf.Max(2, Mathf.CeilToInt(clips[action].length * fps) + 1);
            for (int direction = 0; direction < 6; direction++)
            {
                SetFacing(direction);
                segments.Add(new CaptureSegment { clip = Names[action], facing = direction, firstFrame = outputFrame, frameCount = count });
                for (int frame = 0; frame < count; frame++)
                {
                    Sample(Mathf.Min(frame / (float)fps, clips[action].length));
                    // Advance a rendering frame after pose evaluation, then capture the actual player surface.
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    if (direction == 0 && hips != null)
                        hipsSamples.Add(new HipsSample { clip = Names[action], frame = frame, seconds = time,
                            localPosition = hips.localPosition, worldPosition = hips.position, modelLocalPosition = animator.transform.localPosition });
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    var png = texture.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(captureDirectory, "sequence", outputFrame.ToString("D5") + ".png"), png);
                    if (frame == 0 || frame == (count - 1) / 4 || frame == (count - 1) / 2 || frame == 3 * (count - 1) / 4 || frame == count - 1)
                        File.WriteAllBytes(Path.Combine(captureDirectory, "keyframes", Names[action] + "-d" + direction + "-f" + frame.ToString("D3") + ".png"), png);
                    Destroy(texture);
                    outputFrame++;
                }
            }
            Debug.Log("CAPTURED " + Names[action] + " six directions");
        }
        var manifest = new CaptureManifest { fps = fps, width = Screen.width, height = Screen.height, frameCount = outputFrame, model = modelPrefab.name, boundsMin = motionBounds.min, boundsMax = motionBounds.max, segments = segments.ToArray() };
        File.WriteAllText(Path.Combine(captureDirectory, "capture.json"), JsonUtility.ToJson(manifest, true));
        File.WriteAllText(Path.Combine(captureDirectory, "hips-runtime-samples.json"), JsonUtility.ToJson(new HipsReport { found = hips != null, parent = hips == null || hips.parent == null ? "" : hips.parent.name, applyRootMotion = animator.applyRootMotion, samples = hipsSamples.ToArray() }, true));
        File.WriteAllText(Path.Combine(captureDirectory, "completed.txt"), "frames=" + outputFrame);
        Application.Quit(0);
    }

    [Serializable] sealed class CaptureSegment { public string clip; public int facing, firstFrame, frameCount; }
    [Serializable] sealed class CaptureManifest { public int fps, width, height, frameCount; public string model; public Vector3 boundsMin, boundsMax; public CaptureSegment[] segments; }
    [Serializable] sealed class HipsSample { public string clip; public int frame; public float seconds; public Vector3 localPosition, worldPosition, modelLocalPosition; }
    [Serializable] sealed class HipsReport { public bool found, applyRootMotion; public string parent; public HipsSample[] samples; }
}
