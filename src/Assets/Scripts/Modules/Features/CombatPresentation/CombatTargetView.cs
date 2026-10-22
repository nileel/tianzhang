using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// Structured inspected-target panel. Portrait, identity and the two public resources are
    /// separate bindings; unknown combat facts stay explicitly unexplored.
    /// </summary>
    public sealed class CombatTargetView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Image portrait;
        [SerializeField] private Text nameText;
        [SerializeField] private Text factionText;
        [SerializeField] private Image healthFill;
        [SerializeField] private Text healthLabel;
        [SerializeField] private Image spiritFill;
        [SerializeField] private Text spiritLabel;
        [SerializeField] private Text details;
        [SerializeField] private Button closeButton;
        [SerializeField] private Sprite playerPortrait;
        [SerializeField] private Sprite shijiahouPortrait;
        [SerializeField] private Sprite lieshijiaPortrait;
        private string targetId;
        public string TargetId => targetId;
        public Button CloseButton => closeButton;
        public string NameText => nameText == null ? string.Empty : nameText.text;
        public string FactionText => factionText == null ? string.Empty : factionText.text;
        public string DetailsText => details == null ? string.Empty : details.text;
        public string HealthText => healthLabel == null ? string.Empty : healthLabel.text;
        public string SpiritText => spiritLabel == null ? string.Empty : spiritLabel.text;
        public Sprite PortraitSprite => portrait == null ? null : portrait.sprite;
        public float HealthFillAmount => healthFill == null ? 0f : healthFill.fillAmount;
        public float SpiritFillAmount => spiritFill == null ? 0f : spiritFill.fillAmount;

        public void Configure(Canvas canvas)
        {
            if (canvas == null || root == null || portrait == null || nameText == null || factionText == null ||
                details == null || closeButton == null || healthFill == null || healthLabel == null ||
                spiritFill == null || spiritLabel == null || root.GetComponentInParent<Canvas>() != canvas ||
                canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException("combat_target_references_invalid");
            Hide();
        }

        public void Present(CombatantHudSnapshot unit, bool player)
        {
            if (unit == null) { Hide(); return; }
            targetId = unit.Id;
            factionText.text = player ? "我方" : "敌方";
            nameText.text = unit.DisplayName;
            Sprite sprite = PortraitFor(unit.PortraitKey, player);
            portrait.sprite = sprite;
            portrait.enabled = sprite != null;
            portrait.preserveAspect = true;
            healthFill.fillAmount = unit.MaximumHealth <= 0 ? 0f : Mathf.Clamp01((float)unit.CurrentHealth / unit.MaximumHealth);
            spiritFill.fillAmount = unit.MaximumSpirit <= 0 ? 0f : Mathf.Clamp01((float)unit.CurrentSpirit / unit.MaximumSpirit);
            healthLabel.text = "生命 " + unit.CurrentHealth + "/" + unit.MaximumHealth;
            spiritLabel.text = "灵力 " + unit.CurrentSpirit + "/" + unit.MaximumSpirit;
            // Only the fields already public in this HUD stay visible.
            details.text = "伤害：未探明\n命中：未探明\n能力：未探明";
            root.SetActive(true);
        }

        public void Hide()
        {
            targetId = null;
            if (details != null) details.text = string.Empty;
            if (nameText != null) nameText.text = string.Empty;
            if (factionText != null) factionText.text = string.Empty;
            if (healthLabel != null) healthLabel.text = string.Empty;
            if (spiritLabel != null) spiritLabel.text = string.Empty;
            if (healthFill != null) healthFill.fillAmount = 0f;
            if (spiritFill != null) spiritFill.fillAmount = 0f;
            if (portrait != null)
            {
                portrait.sprite = null;
                portrait.enabled = false;
            }
            if (root != null) root.SetActive(false);
        }

        private Sprite PortraitFor(string key, bool player)
        {
            if (player || string.Equals(key, "player", StringComparison.Ordinal)) return playerPortrait;
            if (string.Equals(key, "enemy_shijiahou", StringComparison.Ordinal)) return shijiahouPortrait;
            if (string.Equals(key, "enemy_lieshijia", StringComparison.Ordinal)) return lieshijiaPortrait;
            return null;
        }

        private void OnDisable() => Hide();
    }
}
