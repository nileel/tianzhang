using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects committed resources and provider-owned head anchors into the existing overlay Canvas.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class CombatUnitHealthBarView : MonoBehaviour
    {
        [Serializable]
        public sealed class Bar
        {
            public RectTransform root;
            public Text identityText;
            public Text healthText;
            public Text spiritText;
            public Image healthFill;
            [NonSerialized] internal string Id;
            [NonSerialized] internal bool Alive;
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
            if (bars == null || bars.Length != 2)
                throw new InvalidOperationException("combat_health_bar_two_slots_required");
            for (int i = 0; i < bars.Length; i++)
            {
                Bar bar = bars[i];
                if (bar == null || bar.root == null || bar.identityText == null || bar.healthText == null ||
                    bar.spiritText == null || bar.healthFill == null || !(bar.root.parent is RectTransform) ||
                    bar.root.GetComponentInParent<Canvas>() != canvas)
                    throw new InvalidOperationException("combat_health_bar_references_invalid");
                if (i > 0 && bars[0].root == bar.root)
                    throw new InvalidOperationException("combat_health_bar_duplicate_slot");
            }
            this.worldCamera = worldCamera;
            this.provider = provider;
            foreach (Bar bar in bars)
            {
                bar.root.anchorMin = new Vector2(0.5f, 0.5f);
                bar.root.anchorMax = new Vector2(0.5f, 0.5f);
                bar.root.pivot = new Vector2(0.5f, 0f);
                foreach (Graphic graphic in bar.root.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
            }
            configured = true;
            Hide();
        }

        public void Present(CombatHudSnapshot snapshot)
        {
            if (!configured) throw new InvalidOperationException("combat_health_bar_not_configured");
            if (snapshot == null) { Hide(); return; }
            if (snapshot.Player != null && snapshot.Enemy != null &&
                string.Equals(snapshot.Player.Id, snapshot.Enemy.Id, StringComparison.Ordinal))
            {
                Debug.LogError("combat_health_bar_input_invalid:duplicate_id", this);
                Hide();
                return;
            }
            foreach (Bar bar in bars)
                if (!HasId(snapshot.Player, bar.Id) && !HasId(snapshot.Enemy, bar.Id)) Clear(bar);
            UpdateResources(snapshot.Player);
            UpdateResources(snapshot.Enemy);
        }

        public void Hide()
        {
            if (bars == null) return;
            foreach (Bar bar in bars)
                if (bar != null) Clear(bar);
        }

        private void OnDisable() => Hide();

        private void UpdateResources(CombatantHudSnapshot value)
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
            bar.identityText.text = value.DisplayName;
            bar.healthText.text = "生命 " + value.CurrentHealth + "/" + value.MaximumHealth;
            bar.spiritText.text = "灵力 " + value.CurrentSpirit + "/" + value.MaximumSpirit;
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
            if (bar.identityText != null) bar.identityText.text = string.Empty;
            if (bar.healthText != null) bar.healthText.text = string.Empty;
            if (bar.spiritText != null) bar.spiritText.text = string.Empty;
            if (bar.healthFill != null) bar.healthFill.fillAmount = 0;
        }
    }
}
