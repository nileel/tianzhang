using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatLogView : MonoBehaviour
    {
        [SerializeField] private Text logText;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button toggleButton;
        [SerializeField] private Button closeButton;
        private bool scrollToLatest;

        private void Awake()
        {
            toggleButton.onClick.AddListener(() => SetExpanded(!panel.activeSelf));
            closeButton.onClick.AddListener(Hide);
        }

        public void Hide() => SetExpanded(false);
        private void SetExpanded(bool expanded)
        {
            if (panel != null) panel.SetActive(expanded);
            if (expanded) scrollToLatest = true;
        }

        public void Clear()
        {
            Hide();
            if (logText != null) logText.text = string.Empty;
            scrollToLatest = true;
        }

        public void Append(string message)
        {
            if (logText == null || string.IsNullOrWhiteSpace(message)) return;
            logText.text = string.IsNullOrEmpty(logText.text) ? message : logText.text + "\n" + message;
            scrollToLatest = true;
        }

        private void LateUpdate()
        {
            if (!scrollToLatest || scrollRect == null || !scrollRect.isActiveAndEnabled) return;
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0;
            scrollToLatest = false;
        }
    }
}
