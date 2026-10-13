using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public static class ConfigFieldGUI
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static decimal DisplayNumber(ConfigFieldDefinition field, ConfigValue value)
        {
            if (!value.HasValue) throw new ArgumentException("空值没有数值。");
            if (field.Scale <= 0m) throw new ArgumentException("单位换算比例必须为正。");
            if (field.Control == ConfigControl.Integer)
                return int.Parse(value.RawToken, NumberStyles.Integer, Invariant) / field.Scale;
            return decimal.Parse(value.RawToken, NumberStyles.Float, Invariant) / field.Scale;
        }

        public static ConfigValue FromDisplayNumber(ConfigFieldDefinition field, string input)
        {
            if (field.Scale <= 0m) throw new ArgumentException("单位换算比例必须为正。");
            if (!decimal.TryParse(input, NumberStyles.Float, Invariant, out var display))
                throw new ArgumentException("请输入有限数值，不接受无穷大或非数值。");
            var stored = checked(display * field.Scale);
            if (field.Control == ConfigControl.Integer && (stored != decimal.Truncate(stored) || stored < int.MinValue || stored > int.MaxValue))
                throw new ArgumentException("数值不能精确换算为32位整数；不得截断或四舍五入。");
            return ConfigValue.Scalar(stored.ToString("G29", Invariant));
        }

        public static void ValidateValue(ConfigFieldDefinition field, ConfigValue value, ConfigFieldContext context = null)
        {
            var expected = field.Control == ConfigControl.List ? ConfigNodeKind.List :
                field.Control == ConfigControl.Group ? ConfigNodeKind.Group : ConfigNodeKind.Scalar;
            if (value.Kind != expected) throw new ArgumentException("控件与字段结构不一致。");
            if (!value.HasValue)
            {
                if (!field.AllowEmpty) throw new ArgumentException("此字段必须填写；零是显式值。");
                return;
            }
            if (value.Kind == ConfigNodeKind.List)
            {
                if (field.Element == null) throw new ArgumentException("列表元素尚未适配。");
                for (var i = 0; i < value.Items.Count; i++) ValidateValue(field.Element, value.Items[i], context?.Child(i.ToString()));
                return;
            }
            if (value.Kind == ConfigNodeKind.Group)
            {
                if (value.Members.Count != field.Children.Count || !field.Children.All(child => value.Members.ContainsKey(child.Column)))
                    throw new ArgumentException("分组字段缺失或存在未知子字段。");
                foreach (var child in field.Children) ValidateValue(child, value.Members[child.Column], context?.Child(child.Column));
                return;
            }
            CsvConfigDocument.ValidateToken(value.RawToken);
            if (value.RawToken.IndexOfAny((field.ReservedDelimiters ?? "").ToCharArray()) >= 0)
                throw new ArgumentException("输入包含此结构保留的分隔符。");
            if (field.Control == ConfigControl.Integer || field.Control == ConfigControl.Decimal) DisplayNumber(field, value);
            if (field.Control == ConfigControl.Boolean && value.RawToken != "0" && value.RawToken != "1")
                throw new ArgumentException("布尔字段只接受显式0或1。");
            if ((field.Control == ConfigControl.Enum || field.Control == ConfigControl.Reference) &&
                !field.GetChoices(context).Any(choice => choice.Id == value.RawToken))
                throw new ArgumentException("引用或枚举不在权威候选中。");
        }

        public static ConfigValue Draw(ConfigFieldDefinition field, ConfigValue value, ConfigValue row,
            string fieldPath, IDictionary<string, string> searches, ConfigFieldContext context = null)
        {
            var editable = field.IsEditable(row, context);
            using (new EditorGUI.DisabledScope(!editable))
            {
                var result = DrawValue(field, value, row, fieldPath, searches, context);
                if (!editable) EditorGUILayout.LabelField(string.IsNullOrEmpty(field.ReadOnlyReason) ? "当前分支不适用，保持原值" : field.ReadOnlyReason, EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(field.Help)) EditorGUILayout.LabelField(field.Help, EditorStyles.wordWrappedMiniLabel);
                return result;
            }
        }

        private static ConfigValue DrawValue(ConfigFieldDefinition field, ConfigValue value, ConfigValue row,
            string path, IDictionary<string, string> searches, ConfigFieldContext context)
        {
            var label = string.IsNullOrEmpty(field.Unit) ? field.ChineseName : $"{field.ChineseName}（{field.Unit}）";
            if (field.Control == ConfigControl.Group)
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                var members = value.Members.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var child in field.Children)
                    members[child.Column] = Draw(child, members[child.Column], row, path + "/" + child.Column, searches, context?.Child(child.Column));
                EditorGUI.indentLevel--;
                return ConfigValue.Group(members, value.RawToken, value.HasValue);
            }
            if (field.Control == ConfigControl.List)
            {
                EditorGUILayout.LabelField($"{label}（{value.Items.Count}项）", EditorStyles.boldLabel);
                if (field.Element == null)
                {
                    EditorGUILayout.HelpBox("列表元素尚未接入适配，不能编辑。", MessageType.Info);
                    return value;
                }
                var items = value.Items.ToList();
                EditorGUI.indentLevel++;
                var removed = -1;
                for (var i = 0; i < items.Count; i++)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField($"第{i + 1}项");
                        items[i] = Draw(field.Element, items[i], row, path + "/" + i, searches, context?.Child(i.ToString()));
                        if (GUILayout.Button("删除此项")) removed = i;
                    }
                }
                if (removed >= 0) items.RemoveAt(removed);
                using (new EditorGUI.DisabledScope(field.NewElement == null))
                    if (GUILayout.Button("添加一项")) items.Add(field.NewElement());
                EditorGUI.indentLevel--;
                if (items.Count == 0) EditorGUILayout.LabelField(field.EmptyMeaning, EditorStyles.wordWrappedMiniLabel);
                return items.SequenceEqual(value.Items) ? value : ConfigValue.List(items, value.RawToken, items.Count > 0);
            }

            if (field.AllowEmpty && field.Control != ConfigControl.Text)
            {
                var specified = EditorGUILayout.Toggle($"{label}：已指定", value.HasValue);
                if (!specified)
                {
                    EditorGUILayout.LabelField(field.EmptyMeaning, EditorStyles.wordWrappedMiniLabel);
                    return value.HasValue ? ConfigValue.Scalar("") : value;
                }
                if (!value.HasValue)
                {
                    var firstChoice = field.GetChoices(context).FirstOrDefault();
                    value = ConfigValue.Scalar(firstChoice?.Id ?? "0");
                }
            }
            if (field.Control == ConfigControl.Boolean)
            {
                var flag = EditorGUILayout.Toggle(label, value.RawToken == "1");
                return flag == (value.RawToken == "1") ? value : ConfigValue.Scalar(flag ? "1" : "0");
            }
            if (field.Control == ConfigControl.Enum || field.Control == ConfigControl.Reference)
            {
                var choices = field.GetChoices(context);
                if (field.Control == ConfigControl.Reference)
                {
                    searches.TryGetValue(path, out var filter);
                    filter = EditorGUILayout.TextField("筛选中文 / ID", filter ?? "");
                    searches[path] = filter;
                    choices = choices.Where(choice => choice.Id == value.RawToken ||
                        choice.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                }
                var index = choices.ToList().FindIndex(choice => choice.Id == value.RawToken);
                var labels = choices.Select(choice => field.Control == ConfigControl.Reference ? choice.DisplayName : choice.ChineseName).ToList();
                if (index < 0) { labels.Insert(0, $"未识别的当前值（{value.RawToken}）"); index = 0; }
                var selected = EditorGUILayout.Popup(label, index, labels.ToArray());
                if (selected == index) return value;
                return ConfigValue.Scalar(choices[selected - (labels.Count > choices.Count ? 1 : 0)].Id);
            }
            if (field.Control == ConfigControl.Integer || field.Control == ConfigControl.Decimal)
            {
                // Unity's floating-point controls cannot preserve exact decimal/Int32 tokens.
                // Delayed invariant decimal input validates the full value without truncation.
                string display;
                try { display = value.HasValue ? DisplayNumber(field, value).ToString("G29", Invariant) : ""; }
                catch (Exception exception) when (exception is FormatException || exception is OverflowException)
                {
                    EditorGUILayout.HelpBox("原值不是合法有限数值；请使用领域校验定位。", MessageType.Error);
                    display = value.RawToken;
                }
                var input = EditorGUILayout.DelayedTextField(label, display);
                return input == display ? value : FromDisplayNumber(field, input);
            }
            var token = EditorGUILayout.DelayedTextField(label, value.RawToken);
            if (field.AllowEmpty && token.Length == 0) EditorGUILayout.LabelField(field.EmptyMeaning, EditorStyles.wordWrappedMiniLabel);
            return token == value.RawToken ? value : ConfigValue.Scalar(token);
        }
    }
}
