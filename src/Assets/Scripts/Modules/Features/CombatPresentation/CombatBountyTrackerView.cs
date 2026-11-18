using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatBountyTrackerView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text summary;
        private Func<IReadOnlyList<CombatBountyTrackerSnapshot>> read;
        private string objective;
        public void SetObjective(string value)
        {
            if (objective == value) return;
            objective = value;
            if (root != null && root.activeSelf) Present(true);
        }
        public string Summary => summary == null ? string.Empty : summary.text;
        public void Configure(Func<IReadOnlyList<CombatBountyTrackerSnapshot>> source) => read = source;
        public void Present(bool visible)
        {
            if (root == null || summary == null) return;
            root.SetActive(visible);
            if (!visible) { summary.text = string.Empty; return; }
            if (!string.IsNullOrEmpty(objective)) { summary.text = "关中阶地 · 演武\n<size=16><color=#e2cc9b>" + objective + "</color></size>"; return; }
            IReadOnlyList<CombatBountyTrackerSnapshot> items = read();
            summary.text = items.Count == 0 ? "悬赏追踪\n暂无已接悬赏" : string.Join("\n", items.Select(item =>
                item.Title + "  " + item.Progress + "/" + item.Required + (item.Completed ? " · 可回城交付" : string.Empty)));
        }
    }
}
