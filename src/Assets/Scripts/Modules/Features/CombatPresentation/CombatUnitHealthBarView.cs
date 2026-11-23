using System;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects committed health and provider-owned head anchors into the existing overlay Canvas as slim bars.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class CombatUnitHealthBarView : MonoBehaviour
    {
        /// <summary>Fixed bar size; the slim bar never stretches with the unit name.</summary>
        public const float BarWidth = 96f;
        public const float BarHeight = 8f;

        [Serializable]
        public sealed class Bar
        {
            public RectTransform root;
            public Image healthFill;
            [NonSerialized] internal string Id;
            [NonSerialized] internal bool Alive;
            [NonSerialized] internal Text Role;
        }

        [SerializeField] private Bar[] bars = Array.Empty<Bar>();
        private Camera worldCamera;
        private Static3DCombatUnitPresentationProvider provider;
        private bool configured;

        public void Configure(Camera worldCamera, Canvas canvas, Static3DCombatUnitPresentationProvider provider)
        {
            if (worldCamera == null) throw new ArgumentNullException(nameof(worldCamera));
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException("combat_health_bar_overlay_canvas_required");
            if (bars == null || bars.Length == 0)
                throw new InvalidOperationException("combat_health_bar_slots_required");
            for (int i = 0; i < bars.Length; i++)
            {
                Bar bar = bars[i];
                if (bar == null || bar.root == null || bar.healthFill == null || !(bar.root.parent is RectTransform) ||
                    bar.root.GetComponentInParent<Canvas>() != canvas)
                    throw new InvalidOperationException("combat_health_bar_references_invalid");
                if (bars.Take(i).Any(prior => prior.root == bar.root))
                    throw new InvalidOperationException("combat_health_bar_duplicate_slot");
            }
            this.worldCamera = worldCamera;
            this.provider = provider;
            foreach (Bar bar in bars)
            {
                bar.root.anchorMin = new Vector2(0.5f, 0.5f);
                bar.root.anchorMax = new Vector2(0.5f, 0.5f);
                bar.root.pivot = new Vector2(0.5f, 0f);
                bar.root.sizeDelta = new Vector2(BarWidth, BarHeight);
                foreach (Graphic graphic in bar.root.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
            }
            configured = true;
            Hide();
        }

        internal void EnableRoleLabels(Font font)
        {
            foreach (Bar bar in bars)
            {
                if (bar.Role != null) continue;
                var role = new GameObject("UnitRole", typeof(RectTransform), typeof(Text), typeof(Outline));
                var rect = (RectTransform)role.transform;
                rect.SetParent(bar.root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
                rect.pivot = new Vector2(1, .5f);
                rect.anchoredPosition = new Vector2(-3, 0);
                rect.sizeDelta = new Vector2(28, 30);
                var label = role.GetComponent<Text>();
                label.font = font;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 22;
                label.alignment = TextAnchor.MiddleCenter;
                label.raycastTarget = false;
                var outline = role.GetComponent<Outline>();
                outline.effectColor = new Color32(21, 20, 20, 255);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                bar.Role = label;
            }
        }

        public void Present(CombatHudSnapshot snapshot)
        {
            if (!configured) throw new InvalidOperationException("combat_health_bar_not_configured");
            if (snapshot == null) { Hide(); return; }
            CombatantHudSnapshot[] units = new[] { snapshot.Player }.Concat(snapshot.Enemies).Where(unit => unit != null).ToArray();
            if (units.Select(unit => unit.Id).Distinct(StringComparer.Ordinal).Count() != units.Length)
            {
                Debug.LogError("combat_health_bar_input_invalid:duplicate_id", this);
                Hide();
                return;
            }
            foreach (Bar bar in bars)
                if (!units.Any(unit => HasId(unit, bar.Id))) Clear(bar);
            foreach (CombatantHudSnapshot unit in units) UpdateResources(unit, snapshot.Timeline);
        }

        public void Hide()
        {
            if (bars == null) return;
            foreach (Bar bar in bars)
                if (bar != null) Clear(bar);
        }

        private void OnDisable() => Hide();

        private void UpdateResources(CombatantHudSnapshot value, CombatTimelineSnapshot timeline)
        {
            if (value == null) return;
            if (string.IsNullOrWhiteSpace(value.Id))
            {
                Debug.LogError("combat_health_bar_input_invalid:missing_id", this);
                return;
            }
            Bar bar = null;
            foreach (Bar candidate in bars)
                if (string.Equals(candidate.Id, value.Id, StringComparison.Ordinal)) { bar = candidate; break; }
            if (bar == null)
                foreach (Bar candidate in bars)
                    if (candidate.Id == null) { bar = candidate; break; }
            if (bar == null) throw new InvalidOperationException("combat_health_bar_slots_exhausted");
            if (value.MaximumHealth <= 0 || value.CurrentHealth < 0 || value.CurrentHealth > value.MaximumHealth ||
                value.MaximumSpirit < 0 || value.CurrentSpirit < 0 || value.CurrentSpirit > value.MaximumSpirit)
            {
                Debug.LogError("combat_health_bar_input_invalid:" + value.Id, this);
                Clear(bar);
                return;
            }
            bar.Id = value.Id;
            bar.Alive = value.CurrentHealth > 0;
            if (bar.Role != null)
            {
                var member = timeline?.Entries.FirstOrDefault(entry => entry.Id == value.Id);
                bool ally = member?.Faction == CombatUnitDisplayFaction.Player;
                bar.Role.text = value.Id == "player" ? "你" : ally ? "友" : member != null ? "敌" : string.Empty;
                bar.Role.color = value.Id == "player" ? new Color32(255, 223, 130, 255) :
                    ally ? new Color32(169, 241, 224, 255) : new Color32(255, 169, 143, 255);
            }
            float ratio = (float)value.CurrentHealth / value.MaximumHealth;
            bar.healthFill.fillAmount = ratio;
            bar.healthFill.color = ratio <= 0.25f ? new Color32(205, 61, 51, 255) : new Color32(78, 162, 103, 255);
            if (!bar.Alive) bar.root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!configured) return;
            foreach (Bar bar in bars)
            {
                Vector3 anchor = default;
                bool visible = bar.Alive && bar.Id != null && provider.TryGetHeadAnchor(bar.Id, out anchor);
                if (visible)
                {
                    Vector3 viewport = worldCamera.WorldToViewportPoint(anchor);
                    visible = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
                    if (visible)
                    {
                        var parent = (RectTransform)bar.root.parent;
                        Vector3 screen = worldCamera.WorldToScreenPoint(anchor);
                        visible = RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out Vector2 local);
                        if (visible) bar.root.anchoredPosition = local - parent.rect.center;
                    }
                }
                bar.root.gameObject.SetActive(visible);
            }
        }

        private static bool HasId(CombatantHudSnapshot value, string id) =>
            id != null && value != null && string.Equals(value.Id, id, StringComparison.Ordinal);

        private static void Clear(Bar bar)
        {
            bar.Id = null;
            bar.Alive = false;
            if (bar.root != null) bar.root.gameObject.SetActive(false);
            if (bar.healthFill != null) bar.healthFill.fillAmount = 0;
        }
    }
}
