using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    [DefaultExecutionOrder(110)]
    public sealed class CombatTargetView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text details;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform worldMarker;
        private Camera worldCamera;
        private Static3DCombatUnitPresentationProvider provider;
        private string targetId;
        public string TargetId => targetId;
        public Button CloseButton => closeButton;
        public RectTransform WorldMarker => worldMarker;
        public string DetailsText => details == null ? string.Empty : details.text;

        public void Configure(Camera camera, Canvas canvas, Static3DCombatUnitPresentationProvider units)
        {
            if (camera == null || canvas == null || units == null || root == null || details == null ||
                closeButton == null || worldMarker == null || worldMarker.GetComponentInParent<Canvas>() != canvas ||
                canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException("combat_target_references_invalid");
            worldCamera = camera;
            provider = units;
            Hide();
        }

        public void Present(CombatantHudSnapshot unit, bool player)
        {
            if (unit == null) { Hide(); return; }
            targetId = unit.Id;
            details.text = (player ? "我方" : "敌方") + " · 查看目标\n" + unit.DisplayName +
                "\n生命 " + unit.CurrentHealth + "/" + unit.MaximumHealth +
                "\n灵力 " + unit.CurrentSpirit + "/" + unit.MaximumSpirit +
                "\n伤害：未探明\n命中：未探明\n能力：未探明";
            root.SetActive(true);
        }

        public void Hide()
        {
            targetId = null;
            if (details != null) details.text = string.Empty;
            if (root != null) root.SetActive(false);
            if (worldMarker != null) worldMarker.gameObject.SetActive(false);
        }

        private void OnDisable() => Hide();

        private void LateUpdate()
        {
            if (worldMarker == null) return;
            if (targetId == null || provider == null || !provider.TryGetVisibleBounds(targetId, out Bounds bounds))
            {
                worldMarker.gameObject.SetActive(false);
                return;
            }
            var parent = (RectTransform)worldMarker.parent;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 screen = worldCamera.WorldToScreenPoint(point);
                if (screen.z <= 0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out Vector2 local))
                {
                    worldMarker.gameObject.SetActive(false);
                    return;
                }
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            worldMarker.anchoredPosition = (min + max) * 0.5f - parent.rect.center;
            worldMarker.sizeDelta = max - min;
            worldMarker.gameObject.SetActive(true);
        }
    }
}
