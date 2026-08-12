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

        public Button BasicAttackButton => basicAttackButton;
        public Button ArtButton => artButton;
        public Button DivineButton => divineButton;
        public Button GuardButton => guardButton;
        public Button WaitButton => waitButton;

        public void Present(bool visible, string artProfileId, string divineProfileId)
        {
            root?.SetActive(visible);
            if (basicAttackButton != null) basicAttackButton.interactable = visible;
            if (guardButton != null) guardButton.interactable = visible;
            if (waitButton != null) waitButton.interactable = visible;
            if (artButton != null) artButton.interactable = visible && !string.IsNullOrWhiteSpace(artProfileId);
            if (divineButton != null) divineButton.interactable = visible && !string.IsNullOrWhiteSpace(divineProfileId);
            if (artLabel != null) artLabel.text = string.IsNullOrWhiteSpace(artProfileId) ? "术法" : artProfileId;
            if (divineLabel != null) divineLabel.text = string.IsNullOrWhiteSpace(divineProfileId) ? "神通" : divineProfileId;
        }
    }
}
