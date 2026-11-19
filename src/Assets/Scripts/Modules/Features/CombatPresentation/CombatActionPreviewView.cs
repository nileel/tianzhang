using System.Globalization;
using System.Linq;
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
            "<color=#238CFF>■</color> 可施放地块   <color=#FF9F3C>■</color> 本次作用";
        private const string SpellTargetingLegend =
            TargetingLegend;

        [SerializeField] private GameObject root;
        [SerializeField] private Text heading;
        [SerializeField] private Text legend;
        [SerializeField] private Text summary;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button swapSlotButton;
        [SerializeField] private Button swapCandidateButton;
        [SerializeField] private Text compactQuote;
        [SerializeField] private Text boardPrompt;
        [SerializeField] private Text actionHint;
        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private Button detailsButton;
        private void Awake()
        {
            if (detailsButton != null) detailsButton.onClick.AddListener(() =>
            { if (IsVisible) detailsRoot.SetActive(!detailsRoot.activeSelf); });
        }
        public Button SwapSlotButton => swapSlotButton;
        public Button SwapCandidateButton => swapCandidateButton;
        public Button ConfirmButton => confirmButton;
        public Button CancelButton => cancelButton;
        public bool IsVisible => root != null && root.activeSelf;
        public static string FormatActionPoints(int units) => (units / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Shows the path quote while hovering, then keeps it visible until confirmation.</summary>
        public void PresentMovement(CombatMovementSnapshot movement, CombatMovementOption hovered, bool selecting,
            bool pointerOnBoard, bool locked = false)
        {
            if (root == null || summary == null) return;
            if (!selecting)
            {
                root.SetActive(false);
                summary.text = string.Empty;
                SetText(heading, string.Empty);
                SetText(legend, string.Empty);
                detailsRoot?.SetActive(false);
                return;
            }
            root.SetActive(true);
            SetConfirmation(locked, locked, "确认移动");
            swapSlotButton?.gameObject.SetActive(false);
            swapCandidateButton?.gameObject.SetActive(false);
            if (cancelButton != null) cancelButton.gameObject.SetActive(true);
            SetText(heading, "移动");
            SetBoardPrompt(!locked);
            SetText(compactQuote, hovered == null ? "按路径报价\n<size=16>选择可达地格</size>" :
                FormatActionPoints(hovered.Cost) + " AP · 0 MP\n<size=16>AP " + FormatActionPoints(movement.RemainingPoints) +
                " → " + FormatActionPoints(movement.RemainingPoints - hovered.Cost) + "</size>");
            SetText(actionHint, pointerOnBoard && hovered == null ? "此处不可到达" : locked ?
                $"已锁定 ({hovered.Destination.Q},{hovered.Destination.R}) · Esc 重选" : "指向预览 · 左键锁定 · Esc 取消");
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
                text.Append("\n左键锁定高亮地块");
                text.Append("\n右键 / Esc / 取消关闭");
            }
            else
            {
                text.Append("目标 (").Append(hovered.Destination.Q).Append(',').Append(hovered.Destination.R)
                    .Append(") · 路径 ").Append(hovered.Path.Count).Append(" 格 · 消耗 ")
                    .Append(FormatActionPoints(hovered.Cost)).Append(" AP");
                text.Append('\n').Append(remaining).Append(" → 预计 ")
                    .Append(FormatActionPoints(movement.RemainingPoints - hovered.Cost)).Append(" AP");
                text.Append(locked ? "\n已锁定，确认移动 · 右键 / Esc 重选" : "\n左键锁定 · 右键 / Esc 取消");
            }
            summary.text = text.ToString();
        }

        public void Present(CombatActionPreviewSnapshot value) => Present(value, -1);

        /// <summary>
        /// Shows the uncommitted action. <paramref name="currentActionPointUnits"/> is the authoritative
        /// remaining AP so the preview can state both the current and the post-action balance.
        /// </summary>
        public void Present(CombatActionPreviewSnapshot value, int currentActionPointUnits, CombatHudSnapshot context = null,
            bool selectingTarget = false, bool priceKnown = true)
        {
            // Scene teardown can destroy the Canvas before the input owner is disabled.
            if (root == null || summary == null || confirmButton == null) return;
            root.SetActive(value != null);
            bool attack = value?.Candidate.Kind is CombatActionKind.BasicAttack or CombatActionKind.Art or CombatActionKind.Divine;
            PresentCompact(value, currentActionPointUnits, selectingTarget, priceKnown);
            SetConfirmation(value != null && !selectingTarget, value?.CanConfirm == true && !selectingTarget,
                value?.Candidate.Kind is CombatActionKind.EndTurn or CombatActionKind.Wait ? "确认结束" :
                value?.Candidate.Kind == CombatActionKind.Guard ? "确认防御" : "确认施行");
            bool swapping = value?.Candidate.Kind == CombatActionKind.SwapSpell;
            bool hasSwapCandidate = context == null || context.Actions.Any(action =>
                action.Candidate.Kind == CombatActionKind.SwapSpell &&
                !context.ArtProfileIds.Contains(action.Candidate.ProfileId));
            swapSlotButton?.gameObject.SetActive(swapping);
            swapCandidateButton?.gameObject.SetActive(swapping);
            if (value == null)
            {
                summary.text = string.Empty;
                SetText(heading, string.Empty);
                SetText(legend, string.Empty);
                detailsRoot?.SetActive(false);
                return;
            }
            bool targeting = value.Candidate.Kind is CombatActionKind.BasicAttack or CombatActionKind.Art or CombatActionKind.Divine;
            SetText(heading, swapping ? "换术" : value.Candidate.Kind == CombatActionKind.BasicAttack ? "普攻" :
                value.Candidate.Kind is CombatActionKind.EndTurn or CombatActionKind.Wait ? "结束" : value.ActionName);
            SetText(legend, value.Candidate.Kind == CombatActionKind.Art
                ? SpellTargetingLegend : targeting ? TargetingLegend : string.Empty);
            if (swapping)
            {
                swapSlotButton.GetComponentInChildren<Text>().text = "槽位 " + (value.Candidate.SlotIndex + 1) + "：" + value.ReplacedSpellName;
                swapCandidateButton.GetComponentInChildren<Text>().text = hasSwapCandidate
                    ? "候选：" + value.ActionName : "暂无可换入项";
                swapCandidateButton.interactable = hasSwapCandidate;
            }
            string cost = FormatActionPoints(value.ActionPointCostUnits) + " AP / " + value.SpiritCost + " MP";
            string settledCt = value.SettledCt.ToString("0.###", CultureInfo.InvariantCulture);
            string threshold = value.Threshold.ToString("0.###", CultureInfo.InvariantCulture);
            string balance = ActionPointLine(currentActionPointUnits, value.ActionPointCostUnits);
            var text = new StringBuilder();
            if (value.Candidate.Kind == CombatActionKind.Art && !value.Candidate.TargetCell.HasValue && string.IsNullOrEmpty(value.Candidate.TargetId))
            {
                text.Append("施法范围 ").Append(value.MinimumRange).Append('–').Append(value.MaximumRange)
                    .Append(" 格 · 作用范围：所选一格");
                text.Append('\n').Append(string.IsNullOrEmpty(value.Reason)
                    ? "左键锁定可施放地块，右键或 Esc 取消" : value.Reason);
                summary.text = text.ToString();
                return;
            }
            if (value.Candidate.Kind == CombatActionKind.BasicAttack &&
                string.IsNullOrEmpty(value.Candidate.TargetId) && value.AttackArea == null)
            {
                text.Append(value.ActionName).Append(" · 指向地块选取方向");
                text.Append('\n').Append(string.IsNullOrEmpty(value.Reason)
                    ? "左键锁定 · 右键 / Esc 取消" : value.Reason);
                if (currentActionPointUnits >= 0)
                    text.Append("\n当前 AP ").Append(FormatActionPoints(currentActionPointUnits));
                summary.text = text.ToString();
                return;
            }
            if (value.AttackArea != null)
            {
                CombatAttackAreaSnapshot area = value.AttackArea;
                text.Append(value.TargetName);
                if (value.TargetDistance >= 0) text.Append(" · 距离 ").Append(value.TargetDistance.ToString("0.###"));
                if (value.Candidate.Kind == CombatActionKind.Art)
                    text.Append(" · 施法范围 ").Append(value.MinimumRange).Append('–').Append(value.MaximumRange);
                text.Append("\n作用 ").Append(area.Cells.Count(cell => cell.IsLegal)).Append(" 格 · 受影响：")
                    .Append(area.Targets.Count == 0 ? area.SceneObjects.Any(item => item.PredictedDamage > 0) ? "环境" : "空放" :
                        string.Join(" / ", area.Targets.Select(target => target.DisplayName)));
                foreach (var item in area.SceneObjects)
                    text.Append(" · ").Append(item.DisplayName).Append(' ').Append(item.Durability).Append('/').Append(item.MaxDurability)
                        .Append(item.PredictedDamage > 0 ? " → " + (item.Durability - item.PredictedDamage) : "（无环境效果）");
                text.Append("\n费用 ").Append(cost).Append(" · CT +").Append(value.CtPenalty)
                    .Append(" · CD ").Append(value.ReuseCooldownTicks)
                    .Append(" · 已用").Append(value.CommittedUseCount).Append(" 次");
                text.Append('\n').Append(value.CanConfirm
                    ? balance + (selectingTarget ? " · 左键锁定" : " · 已锁定，确认施行")
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
                if (context != null)
                    text.Append("已装备：").Append(string.Join(" / ", context.ArtActions.Select(art => art.DisplayName))).Append('\n');
                if (!hasSwapCandidate)
                    text.Append("已学术法均已装备，暂无可替换术法\n");
                text.Append("费用 ").Append(cost).Append(" · CT +").Append(value.CtPenalty)
                    .Append(" · CD ").Append(value.ReuseCooldownTicks).Append(" · 剩余 ")
                    .Append(value.CombatSwapsRemaining).Append(" 次");
                if (hasSwapCandidate)
                    text.Append("\n换入冷却 ").Append(value.ReuseCooldownTicks).Append(" 刻（原剩 ")
                        .Append(value.RemainingCooldownTicks).Append("）· ")
                        .Append(value.CanConfirm ? "成功后退出防御" : "不可施行：" + value.Reason);
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
                if (value.TargetDistance >= 0) text.Append(" · 距离 ").Append(value.TargetDistance.ToString("0.###")).Append(" 格");
                text.Append(" · 施法范围 ").Append(value.MinimumRange).Append('–').Append(value.MaximumRange).Append(" 格");
                text.Append("\n费用 ").Append(cost).Append(" · CT +").Append(value.CtPenalty)
                    .Append(" · CD ").Append(value.ReuseCooldownTicks)
                    .Append(" · 作用范围：所选目标");
                if (balance.Length > 0) text.Append('\n').Append(balance);
                text.Append('\n').Append(value.CanConfirm
                    ? selectingTarget ? "左键锁定 · 右键 / Esc 取消" : "已锁定，确认施行 · Esc 重选"
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

        private void PresentCompact(CombatActionPreviewSnapshot value, int ap, bool selectingTarget, bool priceKnown)
        {
            SetBoardPrompt(selectingTarget);
            if (value == null) return;
            string cost = FormatActionPoints(value.ActionPointCostUnits) + " AP · " + value.SpiritCost + " MP";
            string balance = ap < 0 ? string.Empty : "AP " + FormatActionPoints(ap) + " → " + FormatActionPoints(ap - value.ActionPointCostUnits);
            SetText(compactQuote, priceKnown ? cost + "\n<size=16><color=#bdd4cf>" + balance + "</color></size>" : "选定目标后报价");
            string hint = !value.CanConfirm ? value.Reason : selectingTarget ? value.TargetName + " · 左键锁定" :
                value.TargetName + " · 确认后执行 · Esc 取消";
            SetText(actionHint, hint);
        }

        public void PresentJindan(string title, string compact, string hint, string details, bool canConfirm)
        {
            if (root == null) return;
            root.SetActive(true);
            SetText(heading, title);
            SetText(compactQuote, compact);
            SetText(actionHint, hint);
            SetText(summary, details);
            SetText(legend, string.Empty);
            SetBoardPrompt(false);
            SetConfirmation(true, canConfirm, "确认施行");
            cancelButton?.gameObject.SetActive(true);
            swapSlotButton?.gameObject.SetActive(false);
            swapCandidateButton?.gameObject.SetActive(false);
        }

        public void PresentInvalidated()
        {
            if (root == null) return;
            root.SetActive(true);
            SetText(heading, "失效");
            SetText(compactQuote, "报价已清除");
            SetText(actionHint, "战况已变，请重新选取");
            SetText(summary, "战况已变，请重新选取");
            SetConfirmation(false, false, string.Empty);
        }

        private void SetBoardPrompt(bool visible)
        {
            if (boardPrompt == null) return;
            boardPrompt.gameObject.SetActive(visible);
            if (visible) SetText(boardPrompt, "点格锁定");
        }

        private void SetConfirmation(bool visible, bool available, string caption)
        {
            if (confirmButton == null) return;
            confirmButton.gameObject.SetActive(visible);
            confirmButton.interactable = available;
            if (visible) SetText(confirmButton.GetComponentInChildren<Text>(), caption);
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
