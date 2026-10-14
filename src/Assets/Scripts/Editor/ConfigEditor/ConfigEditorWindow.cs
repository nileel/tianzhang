using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class ConfigEditorWindow : EditorWindow
    {
        private ConfigTableRegistry registry;
        private ConfigDraftSession session;
        private int tableIndex;
        private string rowKey = "";
        private string loadError = "";
        private string inputError = "";
        private ConfigOperationResult operation;
        private Vector2 tableScroll;
        private Vector2 rowScroll;
        private Vector2 formScroll;
        private Vector2 operationScroll;
        [SerializeField] private float rowPanelWidth = 300f;
        private readonly Dictionary<string, string> searches = new Dictionary<string, string>(StringComparer.Ordinal);
        public ConfigDraftSession Session => session;
        public string SelectedTableId => registry == null || registry.Tables.Count == 0 ? "" : registry.Tables[tableIndex].TableId;
        public string SelectedRowKey => rowKey;
        public bool SaveEnabled => registry?.ApplyService != null && session != null && session.IsDirty && session.CanSave &&
            registry.Tables.Count > 0 && registry.Tables[tableIndex].IsAdapted;
        public bool ApplyEnabled => registry?.ApplyService != null && session != null && session.CanSave &&
            registry.Tables.Count > 0 && registry.Tables[tableIndex].IsAdapted &&
            (session.IsDirty || (registry.ApplyService is ConfigSaveApplyService service && service.HasPendingApplication(session)));
        public ConfigOperationResult LastOperation => operation;

        [MenuItem("天章/配置编辑器")]
        public static ConfigEditorWindow Open()
        {
            var window = GetWindow<ConfigEditorWindow>("配置编辑器");
            window.minSize = new Vector2(820, 420);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            if (session == null) LoadProduction();
        }

        private void LoadProduction()
        {
            try
            {
                var discovered = ConfigTableRegistry.Discover();
                Initialize(discovered, discovered.OpenSession());
            }
            catch (Exception exception)
            {
                registry = null; session = null; loadError = $"读取失败：{exception.Message}";
            }
        }

        // Also used to operate isolated in-memory fixtures in the real Editor window.
        public void Initialize(ConfigTableRegistry tableRegistry, ConfigDraftSession draftSession)
        {
            registry = tableRegistry ?? throw new ArgumentNullException(nameof(tableRegistry));
            session = draftSession ?? throw new ArgumentNullException(nameof(draftSession));
            tableIndex = 0; rowKey = ""; loadError = ""; inputError = ""; operation = null;
            searches.Clear();
            if (registry.Tables.Count > 0) SelectTable(registry.Tables[0].TableId);
            RefreshDirtyState();
        }

        public void SelectTable(string tableId)
        {
            var index = registry.Tables.ToList().FindIndex(table => table.TableId == tableId);
            if (index < 0) throw new ArgumentException("未知表。");
            tableIndex = index;
            var table = session.Snapshot().Tables[tableId];
            rowKey = table.SourceRows.OrderBy(pair => pair.Value.PhysicalLine).Select(pair => pair.Key).FirstOrDefault() ?? "";
            inputError = ""; searches.Clear();
            Repaint();
        }

        public void SelectRow(string key)
        {
            if (!session.Snapshot().Tables[SelectedTableId].Rows.ContainsKey(key)) throw new ArgumentException("未知条目。");
            rowKey = key; inputError = ""; searches.Clear(); Repaint();
        }

        public void SetSelectedValue(string fieldPath, ConfigValue value)
        {
            session.SetValue(SelectedTableId, rowKey, fieldPath, value);
            inputError = ""; RefreshDirtyState(); Repaint();
        }

        public void UndoCurrentDraft()
        {
            session.UndoDraft(); inputError = ""; operation = null; RefreshDirtyState(); Repaint();
        }

        private void RefreshDirtyState()
        {
            hasUnsavedChanges = session?.IsDirty ?? false;
            saveChangesMessage = registry?.ApplyService == null
                ? "配置草稿尚未保存，保存服务未接入。选择取消可继续编辑，放弃则撤销本次修改；保存不会关闭窗口或写文件。"
                : "配置草稿尚未保存。选择取消可继续编辑，放弃则撤销本次修改，保存将校验并保存草稿。";
        }

        // Unity's built-in close dialog offers Save / Discard / Cancel. Here Save means
        // continue editing until APPLY exists, never a fake disk save.
        public override void SaveChanges()
        {
            if (SaveEnabled)
            {
                operation = registry.ApplyService.ValidateAndSave(session);
                RefreshDirtyState();
            }
            else
            {
                // Unity refuses native close while hasUnsavedChanges remains true.
                RefreshDirtyState();
            }
        }

        public override void DiscardChanges()
        {
            if (session != null) UndoCurrentDraft();
            base.DiscardChanges();
        }

        private void OnGUI()
        {
            if (!string.IsNullOrEmpty(loadError))
            {
                EditorGUILayout.HelpBox(loadError, MessageType.Error);
                if (GUILayout.Button("重新读取")) LoadProduction();
                return;
            }
            if (session == null || registry == null) return;
            RefreshDirtyState();
            DrawToolbar();
            if (session.IsDirty && registry.ApplyService != null)
            {
                try
                {
                    var linkedSnapshot = session.Snapshot();
                    foreach (var edit in ConfigSaveApplyService.LinkedEdits(session))
                    {
                        var table = linkedSnapshot.Tables[edit.TableId];
                        var field = table.Definition.Fields.FirstOrDefault(f => f.Column == edit.FieldPath.Split('/')[0]);
                        EditorGUILayout.LabelField("保存联动：" + table.Definition.ChineseName + " / " + edit.RowKey + " / " +
                            (field?.ChineseName ?? edit.FieldPath) + "：" + table.Rows[edit.RowKey].At(edit.FieldPath).RawToken + " → " + edit.Value.RawToken,
                            EditorStyles.wordWrappedLabel);
                    }
                }
                catch (Exception exception) { EditorGUILayout.HelpBox("关联预览失败：" + exception.Message, MessageType.Error); }
            }
            if (registry.Tables.Count == 0) { EditorGUILayout.HelpBox("尚无表登记。", MessageType.Info); return; }
            var snapshot = session.Snapshot();
            var current = snapshot.Tables[SelectedTableId];
            using (new EditorGUILayout.HorizontalScope())
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(tableScroll, GUILayout.Width(185)))
                {
                    tableScroll = scroll.scrollPosition;
                    foreach (var table in registry.Tables)
                        if (GUILayout.Toggle(table.TableId == SelectedTableId,
                            table.ChineseName + (table.IsAdapted ? "" : "（待适配）"), "Button"))
                            if (table.TableId != SelectedTableId) { SelectTable(table.TableId); GUIUtility.ExitGUI(); }
                }
                rowPanelWidth = Mathf.Clamp(rowPanelWidth, 180f, Mathf.Max(180f, position.width - 185f - 6f - 320f));
                using (var scroll = new EditorGUILayout.ScrollViewScope(rowScroll, GUILayout.Width(rowPanelWidth)))
                {
                    rowScroll = scroll.scrollPosition;
                    DrawRows(current);
                }
                DrawRowPanelDivider();
                using (var scroll = new EditorGUILayout.ScrollViewScope(formScroll))
                {
                    formScroll = scroll.scrollPosition;
                    DrawForm(current);
                }
            }
            if (!string.IsNullOrEmpty(inputError)) EditorGUILayout.HelpBox(inputError, MessageType.Error);
            if (operation != null)
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(operationScroll, GUILayout.Height(Mathf.Min(180f, position.height * 0.25f))))
                {
                    operationScroll = scroll.scrollPosition;
                    EditorGUILayout.LabelField("已保存文件：" + string.Join("、", operation.SavedPaths), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("未保存文件：" + string.Join("、", operation.FailedSavePaths), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("已应用产物：" + string.Join("、", operation.AppliedPaths), EditorStyles.wordWrappedLabel);
                    if (!string.IsNullOrEmpty(operation.FailedImportStep)) EditorGUILayout.HelpBox("应用失败步骤：" + operation.FailedImportStep, MessageType.Error);
                    foreach (var issue in operation.Issues) EditorGUILayout.HelpBox(
                        $"{issue.TableId} / {issue.RowKey} / {issue.FieldPath}：{issue.ChineseReason}（{issue.ErrorCode}）", MessageType.Error);
                    EditorGUILayout.LabelField(operation.Evidence, EditorStyles.wordWrappedLabel);
                }
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.LabelField(session.IsDirty ? "未保存 *" :
                string.IsNullOrEmpty(session.LastApplicationEvidence) ? "已保存，应用未核验" : "已保存，应用已核验（详细证据见下方）");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!session.IsDirty))
                    if (GUILayout.Button("撤销本次修改", EditorStyles.toolbarButton)) UndoCurrentDraft();
                if (GUILayout.Button("关闭", EditorStyles.toolbarButton))
                {
                    if (TryClose(() => EditorUtility.DisplayDialog("未保存草稿", "放弃全部未保存修改并关闭？", "放弃并关闭", "继续编辑")))
                        GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("重新加载", EditorStyles.toolbarButton) && (!session.IsDirty ||
                    EditorUtility.DisplayDialog("放弃草稿并重新加载", "重新加载会放弃全部未保存修改。", "放弃并重新加载", "继续编辑"))) LoadProduction();
                using (new EditorGUI.DisabledScope(!SaveEnabled))
                    if (GUILayout.Button("校验并保存", EditorStyles.toolbarButton)) RunApply(false);
                using (new EditorGUI.DisabledScope(!ApplyEnabled))
                    if (GUILayout.Button("保存并应用", EditorStyles.toolbarButton)) RunApply(true);
            }
            if (registry.ApplyService == null) EditorGUILayout.HelpBox("保存与应用尚未接入；本窗口只编辑内存草稿。", MessageType.Info);
        }

        private void DrawRowPanelDivider()
        {
            GUILayout.Box(GUIContent.none, GUIStyle.none, GUILayout.Width(6f), GUILayout.ExpandHeight(true));
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            var controlId = GUIUtility.GetControlID("ConfigRowPanelDivider".GetHashCode(), FocusType.Passive, rect);
            var currentEvent = Event.current;
            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (currentEvent.button != 0 || !rect.Contains(currentEvent.mousePosition)) break;
                    GUIUtility.hotControl = controlId;
                    currentEvent.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) break;
                    rowPanelWidth = Mathf.Clamp(rowPanelWidth + currentEvent.delta.x, 180f,
                        Mathf.Max(180f, position.width - 185f - 6f - 320f));
                    currentEvent.Use();
                    Repaint();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId || currentEvent.button != 0) break;
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;
            }
        }

        public bool TryClose(Func<bool> confirmDiscard)
        {
            if (session?.IsDirty == true && !confirmDiscard()) return false;
            DiscardChanges();
            Close();
            return true;
        }

        public void RunApply(bool apply)
        {
            if (apply ? !ApplyEnabled : !SaveEnabled) return;
            try { operation = apply ? registry.ApplyService.SaveAndApply(session) : registry.ApplyService.ValidateAndSave(session); }
            catch (Exception exception) { inputError = "操作失败：" + exception.Message; }
            RefreshDirtyState();
        }

        private void DrawRows(ConfigDraftTableSnapshot table)
        {
            EditorGUILayout.LabelField("已有条目", EditorStyles.boldLabel);
            if (!table.Definition.IsAdapted)
            {
                var lines = table.Document.Lines.Where(line => !line.IsTrivia);
                if (table.Definition.HeaderMode != ConfigHeaderMode.None) lines = lines.Skip(1);
                var count = 0;
                foreach (var line in lines)
                {
                    count++;
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.LabelField(line.Cells[0].Value);
                }
                if (count == 0) EditorGUILayout.LabelField("无已有条目");
                return;
            }
            if (table.Rows.Count == 0) EditorGUILayout.LabelField("无已有条目");
            string lastGroup = null;
            foreach (var row in table.SourceRows.OrderBy(pair => pair.Value.PhysicalLine))
            {
                var group = string.Join(" / ", table.Definition.GroupingFields.Select(column => row.Value[column]));
                if (!string.IsNullOrEmpty(group) && group != lastGroup) EditorGUILayout.LabelField(group, EditorStyles.boldLabel);
                lastGroup = group;
                var label = table.Definition.RowLabel?.Invoke(row.Value) ?? row.Key;
                if (GUILayout.Toggle(row.Key == rowKey, label, "Button") && row.Key != rowKey) SelectRow(row.Key);
            }
        }

        private void DrawForm(ConfigDraftTableSnapshot table)
        {
            EditorGUILayout.LabelField(table.Definition.ChineseName, EditorStyles.boldLabel);
            if (!table.Definition.IsAdapted)
            {
                EditorGUILayout.HelpBox(table.Definition.ReadOnlyReason + "；字段输入禁用。", MessageType.Info);
                return;
            }
            foreach (var issue in table.Issues)
                EditorGUILayout.HelpBox($"{issue.RowKey}：{issue.ChineseReason}", MessageType.Error);
            if (table.Issues.Count > 0) return;
            if (!table.Rows.TryGetValue(rowKey, out var row)) return;
            foreach (var field in table.Definition.Fields)
            {
                try
                {
                    var value = ConfigFieldGUI.Draw(field, row.Members[field.Column], row, field.Column, searches,
                        new ConfigFieldContext(session.Snapshot(), table.Definition.TableId, rowKey, row, field.Column));
                    if (!value.Equals(row.Members[field.Column])) SetSelectedValue(field.Column, value);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
                    exception is FormatException || exception is OverflowException)
                { inputError = field.ChineseName + "：" + exception.Message; }
            }
        }
    }
}
