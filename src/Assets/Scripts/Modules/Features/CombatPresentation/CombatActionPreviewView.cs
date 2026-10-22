using System.Globalization;
using System.Text;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Displays a read-only candidate. All submission stays in CombatCommandInput.</summary>
    public sealed class CombatActionPreviewView : MonoBehaviour
    {
        private const string MovementLegend =
            "<color=#238CFF>■</color> 可移动   <color=#F2C14E>■</color> 目的地   <color=#FFE7A0>■</color> 路径";
        private const string TargetingLegend =
            "<color=#238CFF>■</color> 范围 <color=#E8C46A>■</color> 可选 <color=#FFE7A0>■</color> 当前 " +
            "<color=#FF9F3C>■</color> 作用 <color=#FF4033>■</color> 非法";

        [SerializeField] private GameObject root;
        [SerializeField] private Text heading;
        [SerializeField] private Text legend;
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

        /// <summary>Shows the uncommitted movement candidate; a legal board click commits it.</summary>
        public void PresentMovement(CombatMovementSnapshot movement, CombatMovementOption hovered, bool selecting,
            bool pointerOnBoard)
        {
            if (root == null || summary == null) return;
            if (!selecting)
            {
                root.SetActive(false);
                summary.text = string.Empty;
                SetText(heading, string.Empty);
                SetText(legend, string.Empty);
                return;
            }
            root.SetActive(true);
            // Movement commits from the board, so the generic confirm button never applies.
            confirmButton?.gameObject.SetActive(false);
            swapSlotButton?.gameObject.SetActive(false);
            swapCandidateButton?.gameObject.SetActive(false);
            if (cancelButton != null) cancelButton.gameObject.SetActive(true);
            SetText(heading, "移动预览");
            SetText(legend, MovementLegend);
            if (movement == null)
            {
                summary.text = "移动 · 当前无法行动";
                return;
            }
            string remaining = "当前 AP " + FormatActionPoints(movement.RemainingPoints);
            var text = new StringBuilder();
            if (hovered == null)
            {
                text.Append("可达 ").Append(movement.Options.Count).Append(" 格 · ").Append(remaining);
                text.Append(pointerOnBoard
                    ? "\n此处不可到达：超出移动范围、被阻挡或已占用"
                    : "\n指向青蓝地块预览路径与费用");
                text.Append("\n左键点击高亮地块立即移动");
                text.Append("\n右键 / Esc / 取消关闭");
            }
            else
            {
                text.Append("目标 (").Append(hovered.Destination.Q).Append(',').Append(hovered.Destination.R)
                    .Append(") · 路径 ").Append(hovered.Path.Count).Append(" 格 · 消耗 ")
                    .Append(FormatActionPoints(hovered.Cost)).Append(" AP");
                text.Append('\n').Append(remaining).Append(" → 预计 ")
                    .Append(FormatActionPoints(movement.RemainingPoints - hovered.Cost)).Append(" AP");
                text.Append("\n左键点击立即移动 · 右键 / Esc 取消");
            }
            summary.text = text.ToString();
        }

        public void Present(CombatActionPreviewSnapshot value) => Present(value, -1);

        /// <summary>
        /// Shows the uncommitted action. <paramref name="currentActionPointUnits"/> is the authoritative
        /// remaining AP so the preview can state both the current and the post-action balance.
        /// </summary>
        public void Present(CombatActionPreviewSnapshot value, int currentActionPointUnits)
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
            if (value == null)
            {
                summary.text = string.Empty;
                SetText(heading, string.Empty);
                SetText(legend, string.Empty);
                return;
            }
            bool targeting = value.Candidate.Kind is CombatActionKind.BasicAttack or CombatActionKind.Art or CombatActionKind.Divine;
            SetText(heading, value.ActionName + (targeting ? " · 选择目标" : string.Empty));
            SetText(legend, targeting ? TargetingLegend : string.Empty);
            if (swapping)
            {
                swapSlotButton.GetComponentInChildren<Text>().text = "槽位 " + (value.Candidate.SlotIndex + 1) + "：" + value.ReplacedSpellName;
                swapCandidateButton.GetComponentInChildren<Text>().text = "候选：" + value.ActionName;
            }
            string cost = FormatActionPoints(value.ActionPointCostUnits) + " AP / " + value.SpiritCost + " MP";
            string settledCt = value.SettledCt.ToString("0.###", CultureInfo.InvariantCulture);
            string threshold = value.Threshold.ToString("0.###", CultureInfo.InvariantCulture);
            string balance = ActionPointLine(currentActionPointUnits, value.ActionPointCostUnits);
            var text = new StringBuilder();
            if (value.Candidate.Kind == CombatActionKind.Art && string.IsNullOrEmpty(value.Candidate.TargetId))
            {
                text.Append("施法范围 ").Append(value.MinimumRange).Append('–').Append(value.MaximumRange)
                    .Append(" 格 · 作用范围：所选目标单格");
                text.Append('\n').Append(string.IsNullOrEmpty(value.Reason)
                    ? "左键点击施法范围内角色；右键或 Esc 取消" : value.Reason);
                summary.text = text.ToString();
                return;
            }
            if (value.Candidate.Kind == CombatActionKind.BasicAttack &&
                string.IsNullOrEmpty(value.Candidate.TargetId) && value.AttackArea == null)
            {
                text.Append(value.ActionName).Append(" · 指向地块选取方向");
                text.Append('\n').Append(string.IsNullOrEmpty(value.Reason)
                    ? "左键施行 · 右键 / Esc 取消" : value.Reason);
                if (currentActionPointUnits >= 0)
                    text.Append("\n当前 AP ").Append(FormatActionPoints(currentActionPointUnits));
                summary.text = text.ToString();
                return;
            }
            if (value.AttackArea != null)
            {
                CombatAttackAreaSnapshot area = value.AttackArea;
                text.Append(value.ActionName).Append(" → ").Append(value.TargetName)
                    .Append(" · 命中 ").Append(area.Cells.Count).Append(" 格");
                text.Append("\n费用 ").Append(cost).Append(" · CT +").Append(value.CtPenalty)
                    .Append(" · CD ").Append(value.ReuseCooldownTicks)
                    .Append(" · 已用").Append(value.CommittedUseCount).Append(" 次");
                if (balance.Length > 0) text.Append('\n').Append(balance);
                text.Append('\n').Append(value.CanConfirm
                    ? "左键施行，命中全部目标 · 右键 / Esc 取消"
                    : "不可施行：" + value.Reason);
                summary.text = text.ToString();
                return;
            }
            if (value.Candidate.Kind == CombatActionKind.SwapEquipment && value.EquipmentOwned.HasValue)
            {
                text.Append("当前主手 ").Append(value.CurrentEquipmentName).Append(" → ").Append(value.ActionName)
                    .Append(value.EquipmentOwned.Value ? "（已持有）" : "（未持有）");
                text.Append("\n费用 ").Append(FormatActionPoints(value.ActionPointCostUnits)).Append(" AP · 库存不变");
                text.Append('\n').Append(value.CanConfirm
                    ? "确认后主手与普攻同步；" + (value.RemainsInTurn ? "仍可行动" : "本回合结束")
                    : "不可施行：" + value.Reason);
                if (balance.Length > 0) text.Append('\n').Append(balance);
                summary.text = text.ToString();
                return;
            }
            if (value.Candidate.Kind == CombatActionKind.UseItem)
            {
                text.Append(value.ActionName).Append(" → 自身 · 持有 ").Append(value.ItemAvailableQuantity)
                    .Append(" 颗 · 消耗 ").Append(value.ItemQuantityCost).Append(" 颗");
                text.Append("\n恢复 ").Append(value.ExpectedHealthRecovery).Append(" / 药效 ")
                    .Append(value.ItemHealthRecovery).Append(" · 费用 ")
                    .Append(FormatActionPoints(value.ActionPointCostUnits)).Append(" AP");
                text.Append('\n').Append(value.CanConfirm
                    ? "成功后退出防御；" + (value.RemainsInTurn ? "仍可行动" : "本回合结束")
                    : "不可施行：" + value.Reason);
                if (balance.Length > 0) text.Append('\n').Append(balance);
                summary.text = text.ToString();
                return;
            }
            if (swapping)
            {
                text.Append("费用 ").Append(cost).Append(" · CT +").Append(value.CtPenalty)
                    .Append(" · CD ").Append(value.ReuseCooldownTicks).Append(" · 剩余 ")
                    .Append(value.CombatSwapsRemaining).Append(" 次");
                text.Append("\n换入冷却 ").Append(value.ReuseCooldownTicks).Append(" 刻（原剩 ")
                    .Append(value.RemainingCooldownTicks).Append("）· ")
                    .Append(value.CanConfirm ? "成功后退出防御" : "不可施行：" + value.Reason);
                if (value.CanConfirm)
                    text.Append('\n').Append(value.RemainsInTurn ? "若结束：" : "结束后：")
                        .Append("CT ").Append(settledCt).Append('/').Append(threshold)
                        .Append(" · 约+").Append(value.TicksUntilReady).Append("刻（顺位未定）");
                if (balance.Length > 0) text.Append('\n').Append(balance);
                summary.text = text.ToString();
                return;
            }
            string repeat = attack && value.ReuseCooldownTicks == 0
                ? " · 已用" + value.CommittedUseCount + " 次 · 本次×" +
                    (value.RepeatMultiplierPermille / 1000m).ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty;
            if (value.Candidate.Kind == CombatActionKind.Art)
            {
                text.Append(value.ActionName).Append(" → ").Append(value.TargetName);
                if (value.TargetDistance >= 0) text.Append(" · 距离 ").Append(value.TargetDistance).Append(" 格");
                text.Append(" · 施法范围 ").Append(value.MinimumRange).Append('–').Append(value.MaximumRange).Append(" 格");
                text.Append("\n费用 ").Append(cost).Append(" · CT +").Append(value.CtPenalty)
                    .Append(" · CD ").Append(value.ReuseCooldownTicks)
                    .Append(" · 作用范围：单格");
                if (balance.Length > 0) text.Append('\n').Append(balance);
                text.Append('\n').Append(value.CanConfirm
                    ? "左键施行 · 右键 / Esc 取消"
                    : "不可施行：" + value.Reason + " · 左键不会扣费");
                summary.text = text.ToString();
                return;
            }
            text.Append(value.ActorName).Append(" · ").Append(value.ActionName).Append(" → ").Append(value.TargetName)
                .Append(attack ? " · " + value.AttackDetail : string.Empty).Append(repeat);
            text.Append("\n费用 ").Append(cost).Append(" · CT +").Append(value.CtPenalty)
                .Append(" · CD ").Append(value.ReuseCooldownTicks);
            if (value.CanConfirm)
                text.Append('\n').Append(value.RemainsInTurn ? "若结束：" : "结束后：")
                    .Append("CT ").Append(settledCt).Append('/').Append(threshold)
                    .Append(" · 约+").Append(value.TicksUntilReady).Append("刻（顺位未定）");
            else text.Append('\n').Append("不可施行：").Append(value.Reason);
            if (balance.Length > 0) text.Append('\n').Append(balance);
            summary.text = text.ToString();
        }

        /// <summary>Current AP and the balance after the quoted cost, always derived from authority.</summary>
        private static string ActionPointLine(int currentUnits, int costUnits)
        {
            if (currentUnits < 0 || costUnits <= 0) return string.Empty;
            string current = FormatActionPoints(currentUnits);
            if (currentUnits < costUnits) return "当前 AP " + current + " · 不足（需 " + FormatActionPoints(costUnits) + "）";
            return "当前 AP " + current + " → 预计 " + FormatActionPoints(currentUnits - costUnits) + " AP";
        }

        private static void SetText(Text target, string value)
        {
            if (target != null) target.text = value ?? string.Empty;
        }
    }
}
