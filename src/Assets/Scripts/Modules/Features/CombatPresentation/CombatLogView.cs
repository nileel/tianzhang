using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatLogView : MonoBehaviour
    {
        [SerializeField] private Text logText;
        [SerializeField] private ScrollRect scrollRect;
        private bool scrollToLatest;

        public void Clear()
        {
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
