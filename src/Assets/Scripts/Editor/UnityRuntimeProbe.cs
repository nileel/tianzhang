// Editor-only diagnostics: do not reference project assemblies or business types.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
internal static class UnityRuntimeProbe
{
    private const int SchemaVersion = 1, MaxCleanupPerPoll = 32, MaxRequestFilesPerPoll = 64;
    private const int MaxRequestBytes = 64 * 1024, MaxHierarchyResults = 200, MaxComponents = 32;
    private const int MaxPropertiesPerComponent = 128, MaxPropertiesTotal = 512, MaxStringLength = 1024;
    private const double PollIntervalSeconds = 0.1d, WarningIntervalSeconds = 5d;
    private static readonly TimeSpan OrphanLifetime = TimeSpan.FromSeconds(60);
    private static readonly Regex RequestIdPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly string ProjectPath = NormalizeProjectPath(Path.Combine(Application.dataPath, ".."));
    private static readonly string ProjectGuid = ReadProjectGuid(), ChannelRoot = Path.Combine(ProjectPath, "Library", "UnityRuntimeProbe");
    private static readonly string RequestDirectory = Path.Combine(ChannelRoot, "requests"), ResponseDirectory = Path.Combine(ChannelRoot, "responses");
    private static bool isProcessing;
    private static double nextPollAt, nextWarningAt;
    static UnityRuntimeProbe() { EnsureDirectories(); EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    private static void Poll()
    {
        double now = EditorApplication.timeSinceStartup;
        if (isProcessing || now < nextPollAt)
            return;
        nextPollAt = now + PollIntervalSeconds;
        isProcessing = true;
        try
        {
            EnsureDirectories();
            int cleanupCount = 0;
            var candidates = ScanRequests(ref cleanupCount);
            CleanupOrphans(ref cleanupCount);
            var candidate = candidates
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Request.requestId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate != null)
                ProcessRequest(candidate);
        }
        catch (Exception exception)
        {
            LogWarning($"poll failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            isProcessing = false;
        }
    }
    private static List<RequestCandidate> ScanRequests(ref int cleanupCount)
    {
        var candidates = new List<RequestCandidate>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(RequestDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Take(MaxRequestFilesPerPoll)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return candidates;
        }
        foreach (string path in files)
        {
            string fileId = Path.GetFileNameWithoutExtension(path);
            if (!RequestIdPattern.IsMatch(fileId))
            {
                CleanupInvalid(path, ref cleanupCount, "invalid request filename");
                continue;
            }
            if (!TryReadRequest(path, fileId, out ProbeRequest request, out DateTimeOffset createdAtUtc, out bool expired, out string errorCode, out string errorMessage))
            {
                if (expired)
                    CleanupInvalid(path, ref cleanupCount, null);
                else if (cleanupCount < MaxCleanupPerPoll)
                {
                    WriteResponse(ErrorResponse(fileId, errorCode, errorMessage, false));
                    TryDeleteFile(path);
                    cleanupCount++;
                }
                continue;
            }
            candidates.Add(new RequestCandidate(path, request, createdAtUtc));
        }
        return candidates;
    }
    private static bool TryReadRequest(string path, string fileId, out ProbeRequest request, out DateTimeOffset createdAtUtc, out bool expired, out string errorCode, out string errorMessage)
    {
        request = null;
        createdAtUtc = default;
        expired = false;
        errorCode = "invalid_request";
        errorMessage = "Request is invalid.";
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxRequestBytes)
            {
                errorMessage = "Request size must be between 1 and 65536 bytes.";
                return false;
            }
            request = JsonUtility.FromJson<ProbeRequest>(StrictUtf8.GetString(File.ReadAllBytes(path)));
            if (request == null || request.schemaVersion != SchemaVersion)
            {
                errorCode = "unsupported_schema";
                errorMessage = "schemaVersion must be 1.";
                return false;
            }
            if (!string.Equals(fileId, request.requestId, StringComparison.Ordinal))
            {
                errorCode = "request_id_mismatch";
                errorMessage = "Request filename and requestId must match.";
                return false;
            }
            if (!TryParseUtc(request.createdAtUtc, out createdAtUtc) || !TryParseUtc(request.expiresAtUtc, out DateTimeOffset expiresAtUtc))
            {
                errorMessage = "createdAtUtc and expiresAtUtc must be round-trip UTC timestamps.";
                return false;
            }
            TimeSpan lifetime = expiresAtUtc - createdAtUtc;
            if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromSeconds(30))
            {
                errorMessage = "Request lifetime must be between 1 and 30 seconds.";
                return false;
            }
            if (DateTimeOffset.UtcNow > expiresAtUtc)
            {
                expired = true;
                return false;
            }
            return true;
        }
        catch (FileNotFoundException)
        {
            expired = true;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            expired = true;
            return false;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is DecoderFallbackException || exception is ArgumentException)
        {
            errorMessage = $"Request could not be read: {exception.GetType().Name}.";
            return false;
        }
    }
    private static void ProcessRequest(RequestCandidate candidate)
    {
        ProbeResponse response;
        try
        {
            switch (candidate.Request.action)
            {
                case "status": response = StatusResponse(candidate.Request.requestId); break;
                case "hierarchy": response = HierarchyResponse(candidate.Request); break;
                case "inspect": response = InspectResponse(candidate.Request); break;
                default: response = ErrorResponse(candidate.Request.requestId, "unknown_action", "action must be status, hierarchy, or inspect.", true); break;
            }
        }
        catch (Exception exception)
        {
            response = ErrorResponse(candidate.Request.requestId, "internal_error", $"Probe failed: {exception.GetType().Name}.", true);
        }
        bool written = false;
        try
        {
            WriteResponse(response);
            written = true;
        }
        finally
        {
            if (written)
                TryDeleteFile(candidate.Path);
        }
    }
    private static ProbeResponse StatusResponse(string requestId)
    {
        var response = SuccessResponse(requestId, true);
        response.scenes = ReadScenes();
        return response;
    }
    private static ProbeResponse HierarchyResponse(ProbeRequest request)
    {
        if (request.maxResults < 1 || request.maxResults > MaxHierarchyResults)
            return ErrorResponse(request.requestId, "invalid_max_results", "maxResults must be between 1 and 200.", true);
        var response = SuccessResponse(request.requestId, true);
        response.scenes = ReadScenes();
        foreach (GameObject gameObject in EnumerateSceneObjects(request.scene))
        {
            if (!request.includeInactive && !gameObject.activeInHierarchy)
                continue;
            if (!string.IsNullOrEmpty(request.nameContains) && gameObject.name.IndexOf(request.nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            response.objects.Add(HierarchyObject(gameObject));
            if (response.objects.Count >= request.maxResults)
            {
                response.truncated = true;
                break;
            }
        }
        return response;
    }
    private static ProbeResponse InspectResponse(ProbeRequest request)
    {
        bool byId = request.instanceId != 0;
        bool byPath = !string.IsNullOrWhiteSpace(request.scene) && !string.IsNullOrWhiteSpace(request.hierarchyPath) && request.hierarchyPath.StartsWith("/", StringComparison.Ordinal);
        if (byId == byPath)
            return ErrorResponse(request.requestId, "invalid_selector", "Inspect requires instanceId or scene plus hierarchyPath, but not both.", true);
        List<GameObject> matches;
        if (byId)
        {
            var gameObject = EditorUtility.EntityIdToObject(request.instanceId) as GameObject;
            matches = gameObject != null && IsLoadedSceneObject(gameObject) ? new List<GameObject> { gameObject } : new List<GameObject>();
        }
        else
        {
            matches = EnumerateSceneObjects(request.scene)
                .Where(item => string.Equals(BuildHierarchyPath(item.transform), request.hierarchyPath, StringComparison.Ordinal))
                .Take(9)
                .ToList();
        }
        if (matches.Count == 0)
            return ErrorResponse(request.requestId, "target_not_found", "No loaded scene object matched the selector.", true);
        if (matches.Count > 1)
        {
            var ambiguous = ErrorResponse(request.requestId, "ambiguous_target", "More than one loaded scene object matched the hierarchy path.", true);
            ambiguous.objects.AddRange(matches.Take(8).Select(HierarchyObject));
            ambiguous.truncated = matches.Count > 8;
            return ambiguous;
        }
        var response = SuccessResponse(request.requestId, true);
        response.scenes = ReadScenes();
        response.objects.Add(InspectedObject(matches[0]));
        return response;
    }
    private static IEnumerable<GameObject> EnumerateSceneObjects(string sceneName)
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded || (!string.IsNullOrEmpty(sceneName) && !string.Equals(scene.name, sceneName, StringComparison.OrdinalIgnoreCase)))
                continue;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (GameObject item in EnumerateDepthFirst(root.transform))
                    yield return item;
        }
    }
    private static IEnumerable<GameObject> EnumerateDepthFirst(Transform root)
    {
        yield return root.gameObject;
        for (int index = 0; index < root.childCount; index++)
            foreach (GameObject item in EnumerateDepthFirst(root.GetChild(index)))
                yield return item;
    }
    private static ProbeObject HierarchyObject(GameObject gameObject)
    {
        return new ProbeObject
        {
            instanceId = gameObject.GetInstanceID(), name = gameObject.name, scene = gameObject.scene.name,
            hierarchyPath = BuildHierarchyPath(gameObject.transform), activeSelf = gameObject.activeSelf,
            activeInHierarchy = gameObject.activeInHierarchy, hideFlags = gameObject.hideFlags.ToString(),
            componentTypes = gameObject.GetComponents<Component>().Select(component => component == null ? "<MissingScript>" : component.GetType().FullName).ToList()
        };
    }
    private static ProbeObject InspectedObject(GameObject gameObject)
    {
        ProbeObject result = HierarchyObject(gameObject);
        Transform transform = gameObject.transform;
        result.transform = new ProbeTransform
        {
            position = Vector3Value(transform.position), localPosition = Vector3Value(transform.localPosition),
            rotation = QuaternionValue(transform.rotation), localRotation = QuaternionValue(transform.localRotation),
            localScale = Vector3Value(transform.localScale)
        };
        Component[] components = gameObject.GetComponents<Component>();
        int totalProperties = 0, componentCount = Math.Min(components.Length, MaxComponents);
        result.components = new List<ProbeComponent>();
        for (int componentIndex = 0; componentIndex < componentCount && totalProperties < MaxPropertiesTotal; componentIndex++)
        {
            Component component = components[componentIndex];
            var detail = new ProbeComponent { type = component == null ? "<MissingScript>" : component.GetType().FullName, properties = new List<ProbeProperty>() };
            result.components.Add(detail);
            if (component == null)
                continue;
            var serializedObject = new SerializedObject(component);
            SerializedProperty iterator = serializedObject.GetIterator(); bool enterChildren = true;
            bool hasProperty = iterator.NextVisible(true);
            while (hasProperty && detail.properties.Count < MaxPropertiesPerComponent && totalProperties < MaxPropertiesTotal)
            {
                bool isArray = iterator.isArray && iterator.propertyType != SerializedPropertyType.String;
                detail.properties.Add(ReadProperty(iterator, isArray));
                totalProperties++;
                enterChildren = !isArray;
                hasProperty = iterator.NextVisible(enterChildren);
            }
            detail.truncated = hasProperty;
        }
        result.truncated = components.Length > MaxComponents || totalProperties >= MaxPropertiesTotal || result.components.Any(item => item.truncated);
        return result;
    }
    private static ProbeProperty ReadProperty(SerializedProperty property, bool isArray)
    {
        var result = new ProbeProperty { propertyPath = property.propertyPath, propertyType = property.propertyType.ToString() };
        if (isArray)
        {
            result.arraySize = property.arraySize;
            result.value = $"Count={property.arraySize}";
            return result;
        }
        try
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference || property.propertyType == SerializedPropertyType.ExposedReference)
            {
                UnityEngine.Object reference = property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : property.exposedReferenceValue;
                if (reference != null)
                    result.objectReference = new ProbeReference { instanceId = reference.GetInstanceID(), name = reference.name, type = reference.GetType().FullName };
                result.value = reference == null ? "null" : reference.name;
            }
            else
                result.value = Shorten(FormatPropertyValue(property), out result.truncated);
        }
        catch (Exception exception)
        {
            result.value = $"<unavailable:{exception.GetType().Name}>";
        }
        return result;
    }
    private static string FormatPropertyValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer: return property.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return property.boolValue ? "true" : "false";
            case SerializedPropertyType.Float: return property.doubleValue.ToString("R", CultureInfo.InvariantCulture);
            case SerializedPropertyType.String: return property.stringValue ?? string.Empty;
            case SerializedPropertyType.Color: return property.colorValue.ToString();
            case SerializedPropertyType.LayerMask: return property.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Enum: return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length ? property.enumDisplayNames[property.enumValueIndex] : property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Vector2: return property.vector2Value.ToString();
            case SerializedPropertyType.Vector3: return property.vector3Value.ToString();
            case SerializedPropertyType.Vector4: return property.vector4Value.ToString();
            case SerializedPropertyType.Rect: return property.rectValue.ToString();
            case SerializedPropertyType.Character: return ((char)property.intValue).ToString();
            case SerializedPropertyType.AnimationCurve: return $"Keys={property.animationCurveValue.length}";
            case SerializedPropertyType.Bounds: return property.boundsValue.ToString();
            case SerializedPropertyType.Quaternion: return property.quaternionValue.ToString();
            case SerializedPropertyType.Vector2Int: return property.vector2IntValue.ToString();
            case SerializedPropertyType.Vector3Int: return property.vector3IntValue.ToString();
            case SerializedPropertyType.RectInt: return property.rectIntValue.ToString();
            case SerializedPropertyType.BoundsInt: return property.boundsIntValue.ToString();
            case SerializedPropertyType.ManagedReference: return property.managedReferenceFullTypename ?? "null";
            case SerializedPropertyType.Hash128: return property.hash128Value.ToString();
            default: return property.propertyType.ToString();
        }
    }
    private static List<ProbeScene> ReadScenes()
    {
        var scenes = new List<ProbeScene>();
        Scene active = SceneManager.GetActiveScene();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            scenes.Add(new ProbeScene { name = scene.name, path = scene.path, buildIndex = scene.buildIndex, isLoaded = scene.isLoaded, isDirty = scene.isDirty, isActive = scene == active });
        }
        return scenes;
    }
    private static ProbeResponse SuccessResponse(string requestId, bool includeState)
    {
        return new ProbeResponse { schemaVersion = SchemaVersion, requestId = requestId, status = "ok", generatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), editor = ReadEditor(includeState), scenes = new List<ProbeScene>(), objects = new List<ProbeObject>() };
    }
    private static ProbeResponse ErrorResponse(string requestId, string code, string message, bool includeState)
    {
        ProbeResponse response = SuccessResponse(requestId, includeState);
        response.status = "error";
        response.error = new ProbeError { code = code, message = message };
        return response;
    }
    private static ProbeEditor ReadEditor(bool includeState)
    {
        using (Process process = Process.GetCurrentProcess())
        {
            return new ProbeEditor
            {
                processId = process.Id, processStartTimeUtc = process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                projectPath = ProjectPath, projectGuid = ProjectGuid, unityVersion = Application.unityVersion,
                isPlaying = includeState && EditorApplication.isPlaying, isPaused = includeState && EditorApplication.isPaused,
                isCompiling = includeState && EditorApplication.isCompiling,
                activeScene = includeState ? SceneManager.GetActiveScene().name : string.Empty
            };
        }
    }
    private static void WriteResponse(ProbeResponse response)
    {
        string temporary = Path.Combine(ResponseDirectory, $".{response.requestId}.{Process.GetCurrentProcess().Id}.tmp");
        string final = Path.Combine(ResponseDirectory, response.requestId + ".json");
        TryDeleteFile(temporary);
        File.WriteAllText(temporary, JsonUtility.ToJson(response, false), StrictUtf8);
        TryDeleteFile(final);
        File.Move(temporary, final);
    }
    private static void CleanupInvalid(string path, ref int cleanupCount, string warning)
    {
        if (cleanupCount >= MaxCleanupPerPoll)
            return;
        TryDeleteFile(path);
        cleanupCount++;
        if (!string.IsNullOrEmpty(warning))
            LogWarning(warning);
    }
    private static void CleanupOrphans(ref int cleanupCount)
    {
        DateTime cutoff = DateTime.UtcNow - OrphanLifetime;
        foreach (string directory in new[] { RequestDirectory, ResponseDirectory })
        {
            if (cleanupCount >= MaxCleanupPerPoll || !Directory.Exists(directory))
                break;
            foreach (string path in Directory.EnumerateFiles(directory).Take(MaxRequestFilesPerPoll))
            {
                if (cleanupCount >= MaxCleanupPerPoll)
                    return;
                bool temporary = string.Equals(Path.GetExtension(path), ".tmp", StringComparison.OrdinalIgnoreCase);
                bool orphanResponse = string.Equals(directory, ResponseDirectory, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
                if ((temporary || orphanResponse) && File.GetLastWriteTimeUtc(path) < cutoff)
                    CleanupInvalid(path, ref cleanupCount, null);
            }
        }
    }
    private static void EnsureDirectories() { Directory.CreateDirectory(RequestDirectory); Directory.CreateDirectory(ResponseDirectory); }
    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
    private static bool TryParseUtc(string value, out DateTimeOffset parsed) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed) && parsed.Offset == TimeSpan.Zero;
    private static bool IsLoadedSceneObject(GameObject gameObject) => gameObject.scene.IsValid() && gameObject.scene.isLoaded;
    private static string NormalizeProjectPath(string value)
    {
        string full = Path.GetFullPath(value.TrimEnd()).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string root = Path.GetPathRoot(full);
        while (full.Length > root.Length && full[full.Length - 1] == Path.DirectorySeparatorChar)
            full = full.Substring(0, full.Length - 1);
        return full;
    }
    private static string ReadProjectGuid() { Match match = Regex.Match(File.ReadAllText(Path.Combine(ProjectPath, "ProjectSettings", "ProjectSettings.asset")), @"(?m)^\s*productGUID:\s*([0-9A-Fa-f]{32})\s*$"); if (!match.Success) throw new InvalidOperationException("ProjectSettings.asset has no valid productGUID."); return match.Groups[1].Value.ToLowerInvariant(); }
    private static string BuildHierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        for (Transform current = transform; current != null; current = current.parent)
            names.Push(current.name);
        return "/" + string.Join("/", names.ToArray());
    }
    private static ProbeVector3 Vector3Value(Vector3 value) => new ProbeVector3 { x = value.x, y = value.y, z = value.z };
    private static ProbeQuaternion QuaternionValue(Quaternion value) => new ProbeQuaternion { x = value.x, y = value.y, z = value.z, w = value.w };
    private static string Shorten(string value, out bool truncated)
    {
        value = value ?? string.Empty;
        truncated = value.Length > MaxStringLength;
        return truncated ? value.Substring(0, MaxStringLength) : value;
    }
    private static void LogWarning(string message)
    {
        if (EditorApplication.timeSinceStartup < nextWarningAt)
            return;
        nextWarningAt = EditorApplication.timeSinceStartup + WarningIntervalSeconds;
        UnityEngine.Debug.LogWarning("[UnityRuntimeProbe] " + message);
    }
    private sealed class RequestCandidate
    {
        public readonly string Path;
        public readonly ProbeRequest Request;
        public readonly DateTimeOffset CreatedAtUtc;
        public RequestCandidate(string path, ProbeRequest request, DateTimeOffset createdAtUtc) { Path = path; Request = request; CreatedAtUtc = createdAtUtc; }
    }
#pragma warning disable CS0649 // JsonUtility assigns request DTO fields through reflection.
    [Serializable] private sealed class ProbeRequest { public int schemaVersion; public string requestId; public int clientProcessId; public string createdAtUtc; public string expiresAtUtc; public string action; public string scene; public string nameContains; public bool includeInactive; public int maxResults; public int instanceId; public string hierarchyPath; }
#pragma warning restore CS0649
    [Serializable] private sealed class ProbeResponse { public int schemaVersion; public string requestId; public string status; public string generatedAtUtc; public ProbeEditor editor; public List<ProbeScene> scenes; public List<ProbeObject> objects; public ProbeError error; public bool truncated; }
    [Serializable] private sealed class ProbeEditor { public int processId; public string processStartTimeUtc; public string projectPath; public string projectGuid; public string unityVersion; public bool isPlaying; public bool isPaused; public bool isCompiling; public string activeScene; }
    [Serializable] private sealed class ProbeScene { public string name; public string path; public int buildIndex; public bool isLoaded; public bool isDirty; public bool isActive; }
    [Serializable] private sealed class ProbeObject { public int instanceId; public string name; public string scene; public string hierarchyPath; public bool activeSelf; public bool activeInHierarchy; public string hideFlags; public List<string> componentTypes; public ProbeTransform transform; public List<ProbeComponent> components; public bool truncated; }
    [Serializable] private sealed class ProbeTransform { public ProbeVector3 position; public ProbeVector3 localPosition; public ProbeQuaternion rotation; public ProbeQuaternion localRotation; public ProbeVector3 localScale; }
    [Serializable] private sealed class ProbeVector3 { public float x; public float y; public float z; }
    [Serializable] private sealed class ProbeQuaternion { public float x; public float y; public float z; public float w; }
    [Serializable] private sealed class ProbeComponent { public string type; public List<ProbeProperty> properties; public bool truncated; }
    [Serializable] private sealed class ProbeProperty { public string propertyPath; public string propertyType; public string value; public int arraySize; public ProbeReference objectReference; public bool truncated; }
    [Serializable] private sealed class ProbeReference { public int instanceId; public string name; public string type; }
    [Serializable] private sealed class ProbeError { public string code; public string message; }
}
