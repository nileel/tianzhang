using System.Text;
using TianZhang.Content;
using TianZhang.World;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TianZhang.Features.Settlement
{
    /// <summary>
    /// 册界单据点面板视图：只把按钮翻译为 <see cref="CharterSiteController"/> 的固定顺序动作与
    /// 评估提交，并在每次动作后从 01A progress、目录声明数据与会话长期状态刷新显示。不判断规则、
    /// 不拥有候选或长期状态，不以按钮可用性或显示文本伪造 recognized／connected／registered。
    /// </summary>
    public sealed class CharterSiteView : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text siteText;
        [SerializeField] private Text stepText;
        [SerializeField] private Text identityText;
        [SerializeField] private Text authorizationText;
        [SerializeField] private Text nodeText;
        [SerializeField] private Text supplyText;
        [SerializeField] private Text environmentText;
        [SerializeField] private Text resultText;
        [SerializeField] private Button passageButton;
        [SerializeField] private Button managementButton;
        [SerializeField] private Button nodeButton;
        [SerializeField] private Button registrationButton;
        [SerializeField] private Button supplyButton;
        [SerializeField] private Button jindanButton;
        [SerializeField] private Button yuanyingButton;
        [SerializeField] private Button formalButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private CharterSiteController controller;

        public bool IsOpen => gameObject.activeSelf;
        public string LastResultReason => controller == null ? null : controller.LastReason;

        public void Configure(
            Text title,
            Text site,
            Text step,
            Text identity,
            Text authorization,
            Text node,
            Text supply,
            Text environment,
            Text result,
            Button passage,
            Button management,
            Button nodeConnection,
            Button registration,
            Button prepareSupply,
            Button jindan,
            Button yuanying,
            Button formal,
            Button close,
            CharterSiteController nextController)
        {
            titleText = title;
            siteText = site;
            stepText = step;
            identityText = identity;
            authorizationText = authorization;
            nodeText = node;
            supplyText = supply;
            environmentText = environment;
            resultText = result;
            passageButton = passage;
            managementButton = management;
            nodeButton = nodeConnection;
            registrationButton = registration;
            supplyButton = prepareSupply;
            jindanButton = jindan;
            yuanyingButton = yuanying;
            formalButton = formal;
            closeButton = close;
            controller = nextController;
        }

        public bool Show(
            CharterSiteData site,
            CharterRuleStaticCatalogData staticCatalog,
            ContentCatalogData catalog,
            CharterUseCase useCase,
            string currentSettlementId,
            out string reason)
        {
            if (controller == null)
            {
                gameObject.SetActive(false);
                reason = CharterSiteController.PanelControllerMissingReason;
                return false;
            }
            if (!controller.TryOpen(site, staticCatalog, catalog, useCase, currentSettlementId, out reason))
            {
                gameObject.SetActive(false);
                return false;
            }
            BindButtonListeners();
            gameObject.SetActive(true);
            Refresh();
            return true;
        }

        public void Close()
        {
            if (controller != null)
                controller.Close();
            else
                gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (controller == null || !IsOpen)
                return;
            RefreshTexts();
        }

        private void BindButtonListeners()
        {
            Bind(passageButton, () => controller.SubmitPassage());
            Bind(managementButton, () => controller.SubmitManagement());
            Bind(nodeButton, () => controller.SubmitConnectNodes());
            Bind(registrationButton, () => controller.SubmitRuleEntryRegistration());
            Bind(supplyButton, () => controller.SubmitRealitySupplies());
            Bind(jindanButton, () => controller.SubmitJindan());
            Bind(yuanyingButton, () => controller.SubmitYuanying());
            Bind(formalButton, () => controller.SubmitFormal());
            Bind(closeButton, () => Close());
        }

        private void RefreshTexts()
        {
            CharterSiteData site = controller.Site;
            CharterSiteInteractionProgress progress = controller.Progress;

            SetText(titleText, "旧水驿 · 册界单据点");
            SetText(siteText, "站点: " + UiText.Resolve(site == null ? null : site.siteId) +
                " | 据点: " + UiText.ResolveId("settlement_", site == null ? null : site.settlementId) +
                " | 目录版本: " + controller.CatalogVersion);
            SetText(stepText, "当前步骤: " + UiText.Resolve(controller.CurrentStepId));

            var identity = new StringBuilder();
            identity.Append("通行: ").Append(progress != null && progress.PassageVerified ? "已确认" : "未确认")
                .Append("；管理: ").Append(progress != null && progress.ManagementVerified ? "已确认" : "未确认")
                .Append("；登记条目: ").Append(UiText.Resolve(site == null ? null : site.ruleEntryId))
                .Append("；册界候选: ").Append(string.IsNullOrWhiteSpace(site == null ? null : site.charterCandidateId) ? "未声明" : "已声明");
            SetText(identityText, identity.ToString());

            var authorization = new StringBuilder();
            authorization.Append("授权要求: 已声明 ").Append(controller.RequiredAuthorizationVersionIds.Length).Append(" 项")
                .Append("；登记确认: ").Append(progress != null && progress.RuleEntryRegistrationVerified ? "已完成" : "未完成");
            SetText(authorizationText, authorization.ToString());

            SetText(nodeText, "声明节点: " + controller.DeclaredNodeIds.Length + " 个；已接通: " +
                (progress == null || progress.ConnectedNodeIds == null ? 0 : progress.ConnectedNodeIds.Length) + " 个");
            SetText(supplyText, "声明供给: " + controller.DeclaredSupplyIds.Length + " 项；已准备: " +
                (progress == null || progress.RegisteredRealitySupplyIds == null ? 0 : progress.RegisteredRealitySupplyIds.Length) + " 项");
            SetText(environmentText, BuildEnvironmentLine());
            SetText(resultText, BuildResultLine());
        }

        private string BuildEnvironmentLine()
        {
            CharterRuleInvocationResult evaluation = controller.LastEvaluation;
            if (evaluation == null || evaluation.EmittedEvents == null || evaluation.EmittedEvents.Length == 0)
                return "环境引用: 无";
            return "环境引用: 已生效（" + evaluation.EmittedEvents.Length + " 条事件）";
        }

        private string BuildResultLine()
        {
            var sb = new StringBuilder();
            sb.Append("结果: ").Append(UiText.ReasonDisplay(controller.LastReason, "操作失败"));
            CharterRuleInvocationResult evaluation = controller.LastEvaluation;
            if (evaluation != null && evaluation.ConflictDecision != null)
                sb.Append("；冲突决定: ").Append(ConflictOutcomeDisplay(evaluation.ConflictDecision.Outcome));
            sb.Append("；").Append(BuildLongTermSummary(controller.LongTermState));
            return sb.ToString();
        }

        private static string ConflictOutcomeDisplay(RuleConflictOutcome outcome)
        {
            switch (outcome)
            {
                case RuleConflictOutcome.LeftWins:
                    return "左侧候选获胜";
                case RuleConflictOutcome.RightWins:
                    return "右侧候选获胜";
                case RuleConflictOutcome.Neutral:
                    return "平局";
                case RuleConflictOutcome.Rejected:
                    return "已拒绝";
                case RuleConflictOutcome.Anchored:
                    return "已受锚";
                default:
                    return "未决";
            }
        }

        private static string BuildLongTermSummary(CharterRuntimeStateData state)
        {
            if (state == null)
                return "长期状态: 未接入";
            int registered = state.registeredRuleEntryIds == null ? 0 : state.registeredRuleEntryIds.Length;
            int supplies = state.realitySupplyStates == null ? 0 : state.realitySupplyStates.Length;
            return "长期状态: 已接入（登记条目 " + registered + "，供给 " + supplies + "）";
        }

        private static void SetText(Text label, string value)
        {
            if (label != null)
                label.text = value;
        }

        private static void Bind(Button button, UnityAction action)
        {
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
