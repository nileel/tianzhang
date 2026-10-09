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
        [SerializeField] private Button swapSlotButton;
        [SerializeField] private Button swapCandidateButton;
        public Button SwapSlotButton => swapSlotButton;
        public Button SwapCandidateButton => swapCandidateButton;
        public Button ConfirmButton => confirmButton;
        public Button CancelButton => cancelButton;
        public bool IsVisible => root != null && root.activeSelf;
        public static string FormatActionPoints(int units) => (units / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

        public void Present(CombatActionPreviewSnapshot value)
        {
            // Scene teardown can destroy the Canvas before the input owner is disabled.
            if (root == null || summary == null || confirmButton == null) return;
            root.SetActive(value != null);
            confirmButton.interactable = value != null && value.CanConfirm;
            bool swapping = value?.Candidate.Kind == CombatActionKind.SwapSpell;
            swapSlotButton?.gameObject.SetActive(swapping);
            swapCandidateButton?.gameObject.SetActive(swapping);
            if (value == null) { summary.text = string.Empty; return; }
            if (swapping)
            {
                swapSlotButton.GetComponentInChildren<Text>().text = "槽位 " + (value.Candidate.SlotIndex + 1) + "：" + value.ReplacedSpellName;
                swapCandidateButton.GetComponentInChildren<Text>().text = "候选：" + value.ActionName;
            }
            string ct = value.SettledCt.ToString("0.###", CultureInfo.InvariantCulture);
            string threshold = value.Threshold.ToString("0.###", CultureInfo.InvariantCulture);
            if (value.Candidate.Kind == CombatActionKind.UseItem)
            {
                summary.text = value.ActorName + " · " + value.ActionName + " → 自身\n费用 " +
                    FormatActionPoints(value.ActionPointCostUnits) + " AP · 消耗 " + value.ItemQuantityCost +
                    " 颗（持有 " + value.ItemAvailableQuantity + "）· CT +" + value.CtPenalty +
                    " · 冷却 " + value.ReuseCooldownTicks + " 刻\n生命恢复 " + value.ExpectedHealthRecovery +
                    " / 药效 " + value.ItemHealthRecovery +
                    (value.CanConfirm ? "\n成功后退出防御；" + (value.RemainsInTurn ? "仍可行动" : "本回合结束") :
                        "\n不可施行：" + value.Reason);
                return;
            }
            if (swapping)
            {
                summary.text = value.ActorName + " · 槽位 " + (value.Candidate.SlotIndex + 1) + "：" + value.ReplacedSpellName +
                    " → " + value.ActionName + "\n费用 " + FormatActionPoints(value.ActionPointCostUnits) + " AP / " + value.SpiritCost +
                    " MP · CT +" + value.CtPenalty + " · 本战剩余 " + value.CombatSwapsRemaining + " 次 · 换入冷却 " +
                    value.ReuseCooldownTicks + " 刻（原剩 " + value.RemainingCooldownTicks + "）" +
                    (value.CanConfirm ? "\n成功后退出防御；" + (value.RemainsInTurn ? "仍可行动；若此时结束" : "本回合结束") +
                        " CT " + ct + "/" + threshold + " · 自身下次约 +" + value.TicksUntilReady + " 刻（顺位未定）" :
                        "\n不可施行：" + value.Reason);
                return;
            }
            summary.text = value.ActorName + " · " + value.ActionName + " → " + value.TargetName +
                "\n费用 " + FormatActionPoints(value.ActionPointCostUnits) + " AP / " + value.SpiritCost + " MP · CT 惩罚 +" +
                value.CtPenalty + " · 技能 CD " + value.ReuseCooldownTicks + " 刻（剩 " + value.RemainingCooldownTicks + "）" +
                (value.CanConfirm ? "\n" + (value.RemainsInTurn ? "施行后仍可行动；若现在结束" : "确认结束本回合后") +
                " CT " + ct + "/" + threshold + " · 自身下次约 +" + value.TicksUntilReady + " 刻（顺位未定）" +
                "\n条件：双方存活，速度与CT无后续干预。伤害 / 命中：未探明。" : "\n不可施行：" + value.Reason);
        }
    }
}
