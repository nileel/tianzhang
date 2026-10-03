using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatActionBarView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Button basicAttackButton;
        [SerializeField] private Button artButton;
        [SerializeField] private Button divineButton;
        [SerializeField] private Button guardButton;
        [SerializeField] private Button waitButton;
        [SerializeField] private Text artLabel;
        [SerializeField] private Text divineLabel;
        [SerializeField] private Button secondArtButton;
        [SerializeField] private Text secondArtLabel;
        [SerializeField] private Button secondArtTargetButton;
        [SerializeField] private Text secondArtTargetLabel;
        [SerializeField] private Text actionStatusLabel;
        [SerializeField] private Text waitLabel;
        private Func<string, string> displayReason = value => value;
        private Func<string, string> displayProfile = value => value;

        public void ConfigureText(Func<string, string> profileDisplay, Func<string, string> reasonDisplay = null)
        {
            displayProfile = profileDisplay ?? throw new ArgumentNullException(nameof(profileDisplay));
            displayReason = reasonDisplay ?? (value => value);
        }

        public Button BasicAttackButton => basicAttackButton;
        public Button ArtButton => artButton;
        public Button DivineButton => divineButton;
        public Button SecondArtButton => secondArtButton;
        public Button SecondArtTargetButton => secondArtTargetButton;
        public Button GuardButton => guardButton;
        public Button WaitButton => waitButton;

        public void Present(bool visible, string artProfileId, string divineProfileId)
        {
            root?.SetActive(visible);
            SetEnabled(secondArtButton, false);
            SetEnabled(secondArtTargetButton, false);
            if (secondArtLabel != null) secondArtLabel.text = "术法";
            if (secondArtTargetLabel != null) secondArtTargetLabel.text = "目标：—";
            if (actionStatusLabel != null) actionStatusLabel.text = string.Empty;
            if (waitLabel != null) waitLabel.text = "待机";
            SetEnabled(basicAttackButton, visible);
            SetEnabled(guardButton, visible);
            SetEnabled(waitButton, visible);
            SetEnabled(artButton, visible && !string.IsNullOrWhiteSpace(artProfileId));
            SetEnabled(divineButton, visible && !string.IsNullOrWhiteSpace(divineProfileId));
            if (artLabel != null) artLabel.text = string.IsNullOrWhiteSpace(artProfileId) ? "术法" : displayProfile(artProfileId);
            if (divineLabel != null) divineLabel.text = string.IsNullOrWhiteSpace(divineProfileId) ? "神通" : displayProfile(divineProfileId);
        }

        public void Present(CombatHudSnapshot snapshot, string firstArtTargetId, string secondArtTargetId)
        {
            bool visible = snapshot != null && snapshot.AcceptsCommands;
            bool canAct = visible && !snapshot.CanEndTurn;
            root?.SetActive(visible);
            SetEnabled(basicAttackButton, canAct);
            SetEnabled(guardButton, canAct);
            SetEnabled(waitButton, visible);
            if (waitLabel != null) waitLabel.text = snapshot != null && snapshot.CanEndTurn ? "结束行动" : "待机";
            string divineId = snapshot != null && snapshot.DivineProfileIds.Count > 0 ? snapshot.DivineProfileIds[0] : null;
            if (divineButton != null) divineButton.gameObject.SetActive(!string.IsNullOrWhiteSpace(divineId));
            SetEnabled(divineButton, canAct && !string.IsNullOrWhiteSpace(divineId));
            if (divineLabel != null) divineLabel.text = string.IsNullOrWhiteSpace(divineId) ? "神通" : displayProfile(divineId);

            CombatArtActionSnapshot first = snapshot != null && snapshot.ArtActions.Count > 0 ? snapshot.ArtActions[0] : null;
            CombatArtActionSnapshot second = snapshot != null && snapshot.ArtActions.Count > 1 ? snapshot.ArtActions[1] : null;
            string firstReason = PresentArt(first, firstArtTargetId, artButton, artLabel, canAct);
            string secondReason = PresentArt(second, secondArtTargetId, secondArtButton, secondArtLabel, canAct);
            CombatArtTargetSnapshot secondTarget = FindTarget(second, secondArtTargetId);
            SetEnabled(secondArtTargetButton, canAct && second != null && second.Targets.Count > 1);
            if (secondArtTargetLabel != null)
                secondArtTargetLabel.text = secondTarget == null ? "目标：—" : "目标：" + secondTarget.DisplayName;
            if (actionStatusLabel != null)
                actionStatusLabel.text = !visible ? string.Empty : firstReason +
                    (!string.IsNullOrEmpty(firstReason) && !string.IsNullOrEmpty(secondReason) ? "；" : string.Empty) + secondReason;
        }

        private string PresentArt(CombatArtActionSnapshot art, string targetId, Button button, Text label, bool canAct)
        {
            CombatArtTargetSnapshot target = FindTarget(art, targetId);
            SetEnabled(button, canAct && target != null && target.CanExecute);
            if (label != null)
            {
                label.text = art == null ? "术法" : art.DisplayName + " · " +
                    (target == null ? "无目标" : target.DisplayName) + "\n" +
                    (target == null ? string.Empty : target.SpiritCost + " MP · ") + "CT +" + art.CooldownPenalty;
            }
            if (art == null) return string.Empty;
            if (target == null) return art.DisplayName + "：无可选目标";
            return target.CanExecute || string.IsNullOrEmpty(target.RejectionReason)
                ? string.Empty
                : art.DisplayName + "：" + displayReason(target.RejectionReason);
        }

        private static CombatArtTargetSnapshot FindTarget(CombatArtActionSnapshot art, string targetId)
        {
            if (art == null) return null;
            foreach (CombatArtTargetSnapshot target in art.Targets)
            {
                if (target != null && string.Equals(target.Id, targetId, StringComparison.Ordinal))
                    return target;
            }
            return null;
        }

        private static void SetEnabled(Button button, bool enabled)
        {
            if (button == null) return;
            button.interactable = enabled;
            if (button.image != null) button.image.color = new Color(1, 1, 1, enabled ? 1 : 0.65f);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = enabled ? new Color32(244, 234, 212, 255) : new Color32(177, 191, 187, 255);
        }
    }
}
