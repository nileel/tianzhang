using System.Text;
using TianZhang.Content;
using TianZhang.Game;
using TianZhang.World;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TianZhang.Settlement
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
            GameSession session,
            out string reason)
        {
            if (controller == null)
            {
                gameObject.SetActive(false);
                reason = CharterSiteController.PanelControllerMissingReason;
                return false;
            }
            if (!controller.TryOpen(site, staticCatalog, catalog, session, out reason))
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
            SetText(siteText, "站点: " + SafeId(site == null ? null : site.siteId) +
                " | 据点: " + SafeId(site == null ? null : site.settlementId) +
                " | 目录版本: " + controller.CatalogVersion);
            SetText(stepText, "当前步骤: " + controller.CurrentStepId);

            var identity = new StringBuilder();
            identity.Append("通行: ").Append(SafeId(site == null ? null : site.passageCapabilityId))
                .Append("｜").Append(SafeId(site == null ? null : site.passageOperatorId))
                .Append("→").Append(SafeId(site == null ? null : site.passageTargetId))
                .Append("；管理: ").Append(SafeId(site == null ? null : site.sealManagerId))
                .Append("/").Append(SafeId(site == null ? null : site.sealBeneficiaryId))
                .Append("；条目: ").Append(SafeId(site == null ? null : site.ruleEntryId))
                .Append("；遗物: ").Append(SafeId(controller.AuthorityCharterRelicId))
                .Append("；册界候选: ").Append(SafeId(site == null ? null : site.charterCandidateId));
            SetText(identityText, identity.ToString());

            var authorization = new StringBuilder();
            authorization.Append("授权要求: ").Append(JoinIds(controller.RequiredAuthorizationVersionIds))
                .Append("；站点界印授权: ").Append(SafeId(site == null ? null : site.sealAuthorizationVersionId))
                .Append("；已确认: ").Append(progress != null && progress.RuleEntryRegistrationVerified ? "是" : "否");
            SetText(authorizationText, authorization.ToString());

            SetText(nodeText, "声明节点: " + JoinIds(controller.DeclaredNodeIds) +
                "；已接通: " + JoinIds(progress == null ? null : progress.ConnectedNodeIds));
            SetText(supplyText, "声明供给: " + JoinIds(controller.DeclaredSupplyIds) +
                "；已准备: " + JoinIds(progress == null ? null : progress.RegisteredRealitySupplyIds));
            SetText(environmentText, BuildEnvironmentLine());
            SetText(resultText, BuildResultLine());
        }

        private string BuildEnvironmentLine()
        {
            CharterRuleInvocationResult evaluation = controller.LastEvaluation;
            if (evaluation == null || evaluation.EmittedEvents == null || evaluation.EmittedEvents.Length == 0)
                return "环境引用: 无";
            var sb = new StringBuilder();
            sb.Append("环境引用: ");
            for (int i = 0; i < evaluation.EmittedEvents.Length; i++)
            {
                CharterRuleEventOutput output = evaluation.EmittedEvents[i];
                if (i > 0)
                    sb.Append(", ");
                sb.Append(SafeId(output == null ? null : output.eventId))
                    .Append(":").Append(SafeId(output == null ? null : output.environmentProfileId));
            }
            return sb.ToString();
        }

        private string BuildResultLine()
        {
            var sb = new StringBuilder();
            sb.Append("结果: ").Append(controller.LastReason);
            CharterRuleInvocationResult evaluation = controller.LastEvaluation;
            if (evaluation != null && evaluation.ConflictDecision != null)
            {
                sb.Append("；冲突决定: ").Append(evaluation.ConflictDecision.Outcome)
                    .Append("；获胜候选: ").Append(SafeId(evaluation.ConflictDecision.WinnerCandidateId));
            }
            sb.Append("；").Append(BuildLongTermSummary(controller.LongTermState));
            return sb.ToString();
        }

        private static string BuildLongTermSummary(CharterRuntimeStateData state)
        {
            if (state == null)
                return "长期状态: 未接入";
            var sb = new StringBuilder();
            sb.Append("长期状态: stateId=").Append(SafeId(state.stateId))
                .Append("；遗物=").Append(SafeId(state.charterRelicState))
                .Append("；界印=").Append(SafeId(state.worldSealState))
                .Append("；已登记条目=").Append(JoinIds(state.registeredRuleEntryIds))
                .Append("；当前地区条目=").Append(JoinIds(state.currentRegionRuleEntryIds))
                .Append("；正负提交=").Append(JoinCommitResults(state.positiveCommitResults))
                .Append("/").Append(JoinCommitResults(state.negativeCommitResults))
                .Append("；供给=").Append(JoinSupplies(state.realitySupplyStates))
                .Append("；占用=").Append(JoinOccupancies(state.ruleEntryOccupancies));
            return sb.ToString();
        }

        private static string JoinCommitResults(CharterCommitResultStateData[] values)
        {
            if (values == null || values.Length == 0)
                return "无";
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                CharterCommitResultStateData value = values[i];
                if (value == null)
                    sb.Append("null");
                else
                    sb.Append(SafeId(value.commitId)).Append(":").Append(SafeId(value.resultState));
            }
            return sb.ToString();
        }

        private static string JoinSupplies(CharterRealitySupplyStateData[] values)
        {
            if (values == null || values.Length == 0)
                return "无";
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                CharterRealitySupplyStateData value = values[i];
                if (value == null)
                    sb.Append("null");
                else
                    sb.Append(SafeId(value.realitySupplyId)).Append(":").Append(SafeId(value.state));
            }
            return sb.ToString();
        }

        private static string JoinOccupancies(CharterOccupancyStateData[] values)
        {
            if (values == null || values.Length == 0)
                return "无";
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                CharterOccupancyStateData value = values[i];
                if (value == null)
                    sb.Append("null");
                else
                    sb.Append(SafeId(value.resourceId)).Append(":").Append(SafeId(value.occupancyId));
            }
            return sb.ToString();
        }

        private static string JoinIds(string[] values)
        {
            if (values == null || values.Length == 0)
                return "无";
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(SafeId(values[i]));
            }
            return sb.ToString();
        }

        private static string SafeId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "空" : value;
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
