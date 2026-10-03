using System.Globalization;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Displays a read-only candidate. All submission stays in CombatCommandInput.</summary>
    public sealed class CombatActionPreviewView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text summary;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        public Button ConfirmButton => confirmButton;
        public Button CancelButton => cancelButton;
        public bool IsVisible => root != null && root.activeSelf;

        public void Present(CombatActionPreviewSnapshot value)
        {
            // Scene teardown can destroy the Canvas before the input owner is disabled.
            if (root == null || summary == null || confirmButton == null) return;
            root.SetActive(value != null);
            confirmButton.interactable = value != null && value.CanConfirm;
            if (value == null) { summary.text = string.Empty; return; }
            string ct = value.SettledCt.ToString("0.###", CultureInfo.InvariantCulture);
            string threshold = value.Threshold.ToString("0.###", CultureInfo.InvariantCulture);
            summary.text = value.ActorName + " · " + value.ActionName + " → " + value.TargetName + " · 灵力消耗 " + value.SpiritCost +
                (value.CanConfirm ? "\n" + (value.RemainsInTurn ? "施行后仍可移动；结束本回合后" : "确认结束本回合后") +
                " CT " + ct + "/" + threshold + " · 自身下次约 +" + value.TicksUntilReady + " 刻（顺位未定）" +
                "\n条件：双方存活，速度与CT无后续干预。伤害 / 命中：未探明。" : "\n不可施行：" + value.Reason);
        }
    }
}
