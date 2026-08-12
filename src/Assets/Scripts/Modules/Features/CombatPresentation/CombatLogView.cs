using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatLogView : MonoBehaviour
    {
        [SerializeField] private Text logText;

        public void Clear()
        {
            if (logText != null) logText.text = string.Empty;
        }

        public void Append(string message)
        {
            if (logText == null || string.IsNullOrWhiteSpace(message)) return;
            logText.text = string.IsNullOrEmpty(logText.text) ? message : logText.text + "\n" + message;
        }
    }
}
