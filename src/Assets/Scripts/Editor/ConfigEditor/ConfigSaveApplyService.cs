using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class ConfigSaveApplyService : IConfigApplyService
    {
        private readonly string projectRoot;
        private readonly Action<string, byte[]> write;
        private readonly Action<string, Action> runImport;
        // Window-local, in-memory evidence. Saving a baseline must not erase the pending import scope.
        private readonly Dictionary<ConfigDraftSession, HashSet<string>> pending = new Dictionary<ConfigDraftSession, HashSet<string>>();
        private readonly Dictionary<ConfigDraftSession, Dictionary<string, byte[]>> previousBytes = new Dictionary<ConfigDraftSession, Dictionary<string, byte[]>>();
        public ConfigSaveApplyService() : this(Directory.GetParent(Application.dataPath).FullName) { }
        public ConfigSaveApplyService(string projectRoot, Action<string, byte[]> fileWriter = null, Action<string, Action> importStep = null)
        { this.projectRoot = Path.GetFullPath(projectRoot); write = fileWriter ?? WriteFile; runImport = importStep ?? ((name, action) => action()); }

        public bool HasPendingApplication(ConfigDraftSession session) => pending.TryGetValue(session, out var ids) && ids.Count > 0;
        public IReadOnlyDictionary<string, byte[]> GetPreviousSavedBytes(ConfigDraftSession session) =>
            previousBytes.TryGetValue(session, out var values) ? values.ToDictionary(p => p.Key, p => (byte[])p.Value.Clone()) : new Dictionary<string, byte[]>();
        public ConfigOperationResult ValidateAndSave(ConfigDraftSession session) => Execute(session, false);
        public ConfigOperationResult SaveAndApply(ConfigDraftSession session) => Execute(session, true);

        public static IReadOnlyList<ConfigEdit> LinkedEdits(ConfigDraftSession session)
        {
            var snapshot = session.Snapshot();
            return snapshot.Tables.Values.Where(t => t.Definition.IsAdapted).Select(t => t.Definition.Codec).Distinct()
                .SelectMany(c => c.GetLinkedEdits(snapshot)).GroupBy(e => (e.TableId, e.RowKey, e.FieldPath))
                .Select(group => {
                    if (group.Any(e => !e.Value.Equals(group.First().Value))) throw new InvalidDataException("关联编辑来源冲突：" + group.Key);
                    return group.First();
                }).ToArray();
        }

        private ConfigOperationResult Execute(ConfigDraftSession session, bool apply)
        {
            var result = new ConfigOperationResult();
            var saved = new List<string>();
            var applied = new List<string>();
            string[] changed = Array.Empty<string>();
            string currentTable = "";
            ConfigImportPlan plan = null;
            try
            {
                if (session == null) throw new ArgumentNullException(nameof(session));
                if (!session.CanSave) throw new InvalidDataException("存在未适配字段或文档结构错误，不能保存。");
                CheckSources(session);
                session.ApplyLinkedEdits(LinkedEdits(session));
                var issues = session.ValidateDraft();
                if (issues.Count > 0) { result.Issues = issues; result.Evidence = "未保存：草稿未通过校验。"; return result; }
                changed = session.GetEdits().Select(e => e.TableId).Distinct(StringComparer.Ordinal)
                    .OrderBy(id => session.Snapshot().Tables[id].Definition.CsvPath, StringComparer.Ordinal).ToArray();
                if (!pending.TryGetValue(session, out var waiting)) pending.Add(session, waiting = new HashSet<string>(StringComparer.Ordinal));
                var scope = waiting.Concat(changed).Distinct(StringComparer.Ordinal).ToArray();
                if (scope.Length == 0) { result.Evidence = "已保存，应用未核验；本会话没有待应用变更。"; return result; }
                var snapshot = session.Snapshot();
                var candidates = changed.ToDictionary(id => id, session.EncodeCandidate, StringComparer.Ordinal);
                foreach (var id in changed)
                {
                    currentTable = id;
                    var path = Resolve(snapshot.Tables[id].Definition.CsvPath);
                    if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new IOException("文件为只读：" + path);
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                }
                currentTable = "";
                // Builds pure domain projections before the first CSV replacement.
                plan = ConfigImportPlan.Build(session, scope);
                CheckSources(session);
                if (!previousBytes.TryGetValue(session, out var old)) previousBytes.Add(session, old = new Dictionary<string, byte[]>());
                foreach (var id in changed)
                {
                    currentTable = id;
                    var table = snapshot.Tables[id];
                    CheckSource(table);
                    var path = Resolve(table.Definition.CsvPath);
                    write(path, candidates[id]);
                    // The writer returns only after an exact atomic per-file replacement.
                    saved.Add(table.Definition.CsvPath); waiting.Add(id);
                    old[table.Definition.CsvPath] = table.Document.OriginalBytes;
                    session.AcceptSavedBaseline(id, candidates[id]);
                }
                result.SavedPaths = saved.ToArray();
                result.Evidence = "已保存未应用。";
                if (!apply) return result;
                currentTable = "";
                if (!string.Equals(projectRoot.TrimEnd(Path.DirectorySeparatorChar), Directory.GetParent(Application.dataPath).FullName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("应用必须在当前Unity工程执行，不能导入其他目录的保存结果。");
                var gates = ApplicationGates();
                if (gates.Count != 0)
                { result.Issues = gates; result.Evidence = "已保存未应用：请处理下列阻止原因后显式再次应用。"; return result; }
                CheckSources(session);
                var stepEvidence = new List<string>();
                foreach (var step in plan.Steps)
                {
                    result.FailedImportStep = step.Description.Method;
                    var logs = new List<string>();
                    void OnLog(string message, string trace, LogType type)
                    {
                        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
                            (type == LogType.Warning && (message.IndexOf("skip", StringComparison.OrdinalIgnoreCase) >= 0 || message.Contains("跳")))) logs.Add(message);
                    }
                    Application.logMessageReceived += OnLog;
                    try
                    {
                        CheckSources(session);
                        runImport(step.Description.Method, step.Import);
                        if (logs.Count > 0) throw new InvalidDataException(string.Join("\n", logs));
                        plan.Verify(step);
                        foreach (var path in step.Products.Keys) AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));
                        foreach (var path in step.Products.Keys) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        if (logs.Count > 0) throw new InvalidDataException(string.Join("\n", logs));
                        CheckSources(session); plan.Verify(step);
                        foreach (var pair in plan.Guids)
                            if (AssetDatabase.AssetPathToGUID(pair.Key) != pair.Value) throw new InvalidDataException("依赖GUID变化：" + pair.Key);
                        applied.AddRange(step.Products.Keys);
                        stepEvidence.Add(step.Description.Method + "：已保存并重新加载核验" + step.Products.Count + "项");
                    }
                    catch (Exception exception)
                    {
                        result.Issues = new[] { new ConfigIssue("", "", "", "导入或读回失败：" + exception.Message, "import_failed") };
                        result.AppliedPaths = applied.Distinct(StringComparer.Ordinal).ToArray();
                        result.Evidence = "已保存，应用失败，可能存在部分结果。\n" + string.Join("\n", stepEvidence) +
                            "\n当步可能已更新/未落盘：" + string.Join("、", step.Products.Keys) +
                            "\n未执行：" + string.Join("、", plan.Steps.SkipWhile(s => s != step).Skip(1).Select(s => s.Description.Method));
                        session.RecordApplicationEvidence(""); return result;
                    }
                    finally { Application.logMessageReceived -= OnLog; }
                }
                result.FailedImportStep = "";
                result.AppliedPaths = applied.Distinct(StringComparer.Ordinal).ToArray();
                result.Evidence = "应用成功。\n" + string.Join("\n", stepEvidence) + "\n输入快照：\n" +
                    string.Join("\n", session.Snapshot().Tables.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value.Document.BaselineHash));
                waiting.Clear(); session.RecordApplicationEvidence(result.Evidence);
                return result;
            }
            catch (Exception exception)
            {
                result.SavedPaths = saved.ToArray();
                result.FailedSavePaths = changed.Select(id => session.Snapshot().Tables[id].Definition.CsvPath).Except(saved, StringComparer.Ordinal).ToArray();
                result.Issues = new[] { new ConfigIssue(currentTable, "", "", "保存/应用前检查失败：" + exception.Message, "save_preflight_or_io_failed") };
                result.Evidence = saved.Count == 0 ? (session != null && !session.IsDirty ? "已保存未应用：检查失败，保留诊断。" : "未保存：保留草稿和诊断；没有调用导入器。") :
                    "保存失败：部分CSV已保存，未保存草稿仍保留；未执行导入，不自动回滚或重试。";
                return result;
            }
            finally { plan?.Dispose(); }
        }

        private void CheckSources(ConfigDraftSession session)
        { foreach (var table in session.Snapshot().Tables.Values) CheckSource(table); }
        private void CheckSource(ConfigDraftTableSnapshot table)
        {
            var path = Resolve(table.Definition.CsvPath);
            if (!File.Exists(path) || CsvConfigDocument.ComputeHash(File.ReadAllBytes(path)) != table.Document.BaselineHash)
                throw new IOException("磁盘内容已被外部修改：" + table.Definition.ChineseName + "（" + table.Definition.CsvPath + "）；请确认后重新加载。");
        }
        private string Resolve(string relative)
        {
            if (!relative.StartsWith("Assets/DataConfig/", StringComparison.Ordinal) || !relative.EndsWith(".csv", StringComparison.Ordinal))
                throw new InvalidDataException("配置路径未授权：" + relative);
            var path = Path.GetFullPath(Path.Combine(projectRoot, relative));
            var folder = Path.GetFullPath(Path.Combine(projectRoot, "Assets/DataConfig")) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("配置路径越界。");
            return path;
        }
        public static void WriteFile(string path, byte[] bytes)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(path), ".config-save-" + Guid.NewGuid().ToString("N") + ".tmp~");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (!File.ReadAllBytes(temporary).SequenceEqual(bytes)) throw new IOException("临时文件核验失败。");
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static IReadOnlyList<ConfigIssue> ApplicationGates()
        {
            var issues = new List<ConfigIssue>();
            void Issue(string path, string message) => issues.Add(new ConfigIssue("", "", path, message, "application_gate"));
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                Issue("", "播放、编译或资源更新期间不能应用配置。");
            foreach (var asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
                if (asset != null && EditorUtility.IsPersistent(asset) && EditorUtility.IsDirty(asset))
                {
                    var path = AssetDatabase.GetAssetPath(asset);
                    // Imported objects (e.g. shaders) carry import dirtiness, not unsaved serialized edits.
                    if ((path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)) &&
                        (AssetDatabase.IsNativeAsset(asset) || asset is AssetImporter || PrefabUtility.IsPartOfPrefabAsset(asset)))
                        Issue(path, "资产有未保存修改，请自行保存或撤销。");
                }
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            { var scene = EditorSceneManager.GetSceneAt(i); if (scene.isDirty) Issue(scene.path, "场景有未保存修改，请自行保存或撤销。"); }
            return issues;
        }
    }
}
