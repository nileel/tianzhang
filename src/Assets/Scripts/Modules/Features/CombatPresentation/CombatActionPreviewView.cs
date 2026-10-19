using System.Globalization;
using System.Linq;
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
            bool attack = value?.Candidate.Kind is CombatActionKind.BasicAttack or CombatActionKind.Art or CombatActionKind.Divine;
            confirmButton.gameObject.SetActive(!attack);
            confirmButton.interactable = !attack && value != null && value.CanConfirm;
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
            if (value.Candidate.Kind == CombatActionKind.Art && string.IsNullOrEmpty(value.Candidate.TargetId))
            {
                summary.text = value.ActionName + " · 待选目标" +
                    "\n施法范围 " + value.MinimumRange + "–" + value.MaximumRange + " 格 · 作用范围：所选目标所在单格" +
                    "\n" + value.Reason;
                return;
            }
            if (value.AttackArea != null)
            {
                CombatAttackAreaSnapshot area = value.AttackArea;
                string Hex(CombatUnitPresentationHex hex) => "(" + hex.Q + "," + hex.R + ")";
                string castRange = value.MaximumRange > 0
                    ? " · 施法 " + value.MinimumRange + "–" + value.MaximumRange + " 格" : string.Empty;
                summary.text = value.ActionName + " · 起点" + Hex(area.Origin) + " · 方向" + area.Direction + castRange + " · " +
                    string.Join(" / ", area.Cells.Select(cell => Hex(cell.Position) + (cell.IsLegal ? "可命中" : cell.Reason))) +
                    "\n目标：" + (area.Targets.Count == 0 ? "无" : string.Join("、", area.Targets.Select(target => target.DisplayName + "[" + target.Id + "]"))) +
                    " · 一次费用 " + FormatActionPoints(value.ActionPointCostUnits) + " AP" +
                    "\n" + (value.CanConfirm ? "左键施行，命中全部目标" : "不可施行：" + value.Reason) +
                    " · 已用" + value.CommittedUseCount + "次 · 本次×" +
                    (value.RepeatMultiplierPermille / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
                return;
            }
            if (value.Candidate.Kind == CombatActionKind.SwapEquipment && value.EquipmentOwned.HasValue)
            {
                summary.text = "当前主手：" + value.CurrentEquipmentName + " → " + value.ActionName +
                    (value.EquipmentOwned.Value ? "（已持有）" : "（未持有）") +
                    "\n费用 " + FormatActionPoints(value.ActionPointCostUnits) + " AP；库存不变" +
                    (value.CanConfirm ? "\n确认后主手与普攻同步；" + (value.RemainsInTurn ? "仍可行动" : "本回合结束") :
                        "\n不可施行：" + value.Reason);
                return;
            }
            if (value.Candidate.Kind == CombatActionKind.UseItem)
            {
                summary.text = value.ActionName + " → 自身 · 持有 " + value.ItemAvailableQuantity + " 颗" +
                    "\n恢复 " + value.ExpectedHealthRecovery + " / 药效 " + value.ItemHealthRecovery +
                    " · 消耗 " + value.ItemQuantityCost + " 颗 / " + FormatActionPoints(value.ActionPointCostUnits) + " AP" +
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
            string repeat = attack && value.ReuseCooldownTicks == 0
                ? " · 已用" + value.CommittedUseCount + "次 · 本次×" +
                    (value.RepeatMultiplierPermille / 1000m).ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty;
            string artRange = value.Candidate.Kind == CombatActionKind.Art
                ? "\n施法范围 " + value.MinimumRange + "–" + value.MaximumRange + " 格 · 作用范围：所选目标所在单格" : string.Empty;
            summary.text = value.ActorName + " · " + value.ActionName + " → " + value.TargetName +
                (attack ? " · " + value.AttackDetail + repeat : string.Empty) + artRange +
                "\n费用 " + FormatActionPoints(value.ActionPointCostUnits) + " AP / " + value.SpiritCost + " MP · CT 惩罚 +" +
                value.CtPenalty + " · CD " + value.ReuseCooldownTicks + "（剩 " + value.RemainingCooldownTicks + " 刻）" +
                (value.CanConfirm ? "\n" + (value.RemainsInTurn ? "若结束：" : "结束后：") +
                    "CT " + ct + "/" + threshold + " · 约+" + value.TicksUntilReady + "刻（双方存活、速度/CT不变，顺位未定）" :
                    "\n不可施行：" + value.Reason);
        }
    }
}
