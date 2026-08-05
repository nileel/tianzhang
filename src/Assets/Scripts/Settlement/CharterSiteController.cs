using System;
using System.Collections.Generic;
using TianZhang.Content;
using TianZhang.Game;
using TianZhang.World;
using UnityEngine;

namespace TianZhang.Settlement
{
    /// <summary>
    /// 册界单据点控制器：只把面板按钮翻译为 01A 交互桥的固定顺序动作与三次评估，正式结果只经
    /// <see cref="GameSession.CommitCharterFormalResult"/> 唯一提交入口写入会话。不复制 01A 规则、
    /// 不拥有候选或长期状态，candidate 永不赋值给会话；关闭面板即丢弃本次交互的临时 progress。
    /// 显示所需的只读数据（站点声明、目录声明集合、01A progress、评估结果与会话长期状态）全部
    /// 来自真实事实源，不以按钮可用性或本地布尔代替实际结果。
    /// </summary>
    public sealed class CharterSiteController : MonoBehaviour
    {
        /// <summary>最小 UI 切片使用声明的评估世界刻；位于站点 grant 声明窗口 [0, 500] 内，与 01A 直接测试同值。</summary>
        public const int EvaluationWorldTick = 100;
        public const string PositiveCommitResultState = "applied";
        public const string NegativeCommitResultState = "applied";
        public const string FormalCommittedReason = "charter_panel_formal_committed";
        public const string PanelControllerMissingReason = "charter_panel_controller_missing";

        [SerializeField] private CharterSiteView view;

        private CharterSiteData site;
        private CharterRuleStaticCatalogData staticCatalog;
        private ContentCatalogData catalog;
        private GameSession session;
        private CharterSiteInteractionRuntime interaction;
        private CharterInvocationPreparation preparation;
        private CharterRuleInvocationResult lastEvaluation;
        private string lastReason = CharterSiteInteractionReasons.Ok;

        public bool IsOpen => gameObject.activeSelf;
        public string LastReason => lastReason;
        public CharterSiteData Site => site;
        public GameSession Session => session;
        public CharterRuleStaticCatalogData StaticCatalog => staticCatalog;
        public CharterSiteInteractionProgress Progress => interaction == null ? null : interaction.Progress;
        public CharterRuleInvocationResult LastEvaluation => lastEvaluation;
        public CharterRuntimeStateData LongTermState => session == null ? null : session.CharterRuntimeState;
        public int CatalogVersion => staticCatalog == null ? 0 : staticCatalog.DefinitionCatalogVersion;

        public void Configure(CharterSiteView nextView)
        {
            view = nextView;
        }

        /// <summary>
        /// 打开面板的唯一门禁是 01A 失败关闭入口：站点必须存在、属于当前据点且唯一静态目录可校验；
        /// 任一不满足返回稳定原因且不打开面板、不推进 progress。每次打开都是全新的一次交互。
        /// </summary>
        public bool TryOpen(
            CharterSiteData nextSite,
            CharterRuleStaticCatalogData nextStaticCatalog,
            ContentCatalogData nextCatalog,
            GameSession nextSession,
            out string reason)
        {
            if (nextSite == null || nextStaticCatalog == null || nextCatalog == null || nextSession == null)
            {
                ResetInteraction();
                lastReason = CharterSiteInteractionReasons.SiteUnavailable;
                reason = lastReason;
                RefreshView();
                return false;
            }

            if (!CharterSiteInteractionRuntime.TryCreate(
                    nextSite,
                    nextStaticCatalog,
                    nextSession.CurrentSettlementId,
                    out CharterSiteInteractionRuntime nextInteraction,
                    out reason))
            {
                ResetInteraction();
                lastReason = reason;
                RefreshView();
                return false;
            }

            site = nextSite;
            staticCatalog = nextStaticCatalog;
            catalog = nextCatalog;
            session = nextSession;
            interaction = nextInteraction;
            preparation = null;
            lastEvaluation = null;
            lastReason = CharterSiteInteractionReasons.Ok;
            RefreshView();
            return true;
        }

        /// <summary>步骤 1：使用《开阖九章》识别并开启站点声明门禁；动作输入只取站点声明身份。</summary>
        public CharterInteractionActionResult SubmitPassage()
        {
            if (interaction == null || site == null)
                return RecordActionResult(CharterSiteInteractionReasons.SiteUnavailable);
            return RecordActionResult(interaction.VerifyPassage(
                site.passageCapabilityId, site.passageOperatorId, site.passageTargetId));
        }

        /// <summary>步骤 2：确认太玄界印管理／受益；通行成功不能自动获得管理权。</summary>
        public CharterInteractionActionResult SubmitManagement()
        {
            if (interaction == null || site == null)
                return RecordActionResult(CharterSiteInteractionReasons.SiteUnavailable);
            return RecordActionResult(interaction.VerifyManagement(site.sealManagerId, site.sealBeneficiaryId));
        }

        /// <summary>步骤 3：接通册界、水工、河道／湿地节点；提交集合只取定义声明的锚点集合。</summary>
        public CharterInteractionActionResult SubmitConnectNodes()
        {
            if (interaction == null || site == null)
                return RecordActionResult(CharterSiteInteractionReasons.SiteUnavailable);
            return RecordActionResult(interaction.ConnectNodes(DeclaredNodeIds));
        }

        /// <summary>步骤 4：登记水府地纪并确认天章实例与授权版本；遗物与授权集合只取权威要求声明。</summary>
        public CharterInteractionActionResult SubmitRuleEntryRegistration()
        {
            if (interaction == null || site == null)
                return RecordActionResult(CharterSiteInteractionReasons.SiteUnavailable);
            return RecordActionResult(interaction.VerifyRuleEntryRegistration(
                site.ruleEntryId, AuthorityCharterRelicId, RequiredAuthorizationVersionIds));
        }

        /// <summary>步骤 5：准备正负提交声明的现实供给集合。</summary>
        public CharterInteractionActionResult SubmitRealitySupplies()
        {
            if (interaction == null || site == null)
                return RecordActionResult(CharterSiteInteractionReasons.SiteUnavailable);
            return RecordActionResult(interaction.PrepareRealitySupplies(DeclaredSupplyIds));
        }

        /// <summary>金丹评估：同一 preparation 派生请求，稳定返回册界侧未获胜；不提交任何状态。</summary>
        public CharterRuleInvocationResult SubmitJindan()
        {
            if (!TryEnsurePreparation(out string reason))
            {
                RecordActionResult(reason);
                return null;
            }
            return RecordEvaluationResult(interaction.EvaluateJindan(
                preparation, EvaluationWorldTick, PositiveCommitResultState, NegativeCommitResultState));
        }

        /// <summary>元婴评估：同一 preparation 派生受锚请求，稳定返回受锚且不降格金丹冲突；不提交。</summary>
        public CharterRuleInvocationResult SubmitYuanying()
        {
            if (!TryEnsurePreparation(out string reason))
            {
                RecordActionResult(reason);
                return null;
            }
            return RecordEvaluationResult(interaction.EvaluateYuanying(
                preparation, EvaluationWorldTick, PositiveCommitResultState, NegativeCommitResultState));
        }

        /// <summary>
        /// 正式调用：求值绑定会话当前长期状态（未接入时以 candidate 自举一次），成功结果只经
        /// <see cref="GameSession.CommitCharterFormalResult"/> 唯一提交入口写入；candidate 永不直接
        /// 赋值给会话。求值结果保留给面板显示环境引用，提交拒绝仍显示稳定原因。
        /// </summary>
        public CharterInvocationCommitResult SubmitFormal()
        {
            if (!TryEnsurePreparation(out string reason))
            {
                RecordActionResult(reason);
                return null;
            }
            CharterRuleInvocationResult result = interaction.EvaluateFormal(
                preparation,
                session.CharterRuntimeState,
                EvaluationWorldTick,
                PositiveCommitResultState,
                NegativeCommitResultState);
            if (!result.Succeeded)
            {
                RecordEvaluationResult(result);
                return null;
            }

            CharterInvocationCommitResult commit = session.CommitCharterFormalResult(
                catalog, result, preparation.CatalogVersion);
            lastEvaluation = result;
            lastReason = commit.Succeeded ? FormalCommittedReason : commit.Reason;
            RefreshView();
            return commit;
        }

        /// <summary>关闭面板：按 01A 契约丢弃本次交互的临时 progress 与 candidate，不写会话、不保存。</summary>
        public void Close()
        {
            ResetInteraction();
            lastReason = CharterSiteInteractionReasons.Ok;
            RefreshView();
            gameObject.SetActive(false);
        }

        /// <summary>当前步骤由真实 progress 与长期状态推导，不保存 UI 步骤计数器。</summary>
        public string CurrentStepId
        {
            get
            {
                if (interaction == null)
                    return "charter_step_unopened";
                if (LongTermState != null)
                    return "charter_step_committed";
                CharterSiteInteractionProgress p = interaction.Progress;
                if (!p.PassageVerified)
                    return "charter_step_passage";
                if (!p.ManagementVerified)
                    return "charter_step_management";
                if (p.ConnectedNodeIds == null || p.ConnectedNodeIds.Length == 0)
                    return "charter_step_nodes";
                if (!p.RuleEntryRegistrationVerified)
                    return "charter_step_registration";
                if (p.RegisteredRealitySupplyIds == null || p.RegisteredRealitySupplyIds.Length == 0)
                    return "charter_step_supplies";
                return "charter_step_evaluation";
            }
        }

        /// <summary>站点条目在唯一静态目录中的定义；只读，用于显示目录声明集合。</summary>
        public CharterRuleDefinitionData Definition
        {
            get
            {
                if (staticCatalog == null || site == null)
                    return null;
                foreach (CharterRuleDefinitionData definition in
                         staticCatalog.Definitions ?? Array.Empty<CharterRuleDefinitionData>())
                {
                    if (definition != null &&
                        string.Equals(definition.ruleEntryId, site.ruleEntryId, StringComparison.Ordinal))
                    {
                        return definition;
                    }
                }
                return null;
            }
        }

        /// <summary>权威要求声明的天章实例遗物 ID；登记动作与显示只使用该声明。</summary>
        public string AuthorityCharterRelicId
        {
            get
            {
                CharterAuthorityRequirement authority = FindAuthority();
                return authority == null ? null : authority.relicId;
            }
        }

        /// <summary>权威要求声明的授权版本集合（授权缺口显示与登记动作输入）。</summary>
        public string[] RequiredAuthorizationVersionIds
        {
            get
            {
                CharterAuthorityRequirement authority = FindAuthority();
                if (authority == null || authority.organizationAuthorizationVersionIds == null)
                    return Array.Empty<string>();
                return (string[])authority.organizationAuthorizationVersionIds.Clone();
            }
        }

        /// <summary>定义声明的锚点节点集合（显示与接通动作输入）。</summary>
        public string[] DeclaredNodeIds
        {
            get
            {
                CharterRuleDefinitionData definition = Definition;
                return definition == null || definition.anchorNodeIds == null
                    ? Array.Empty<string>()
                    : (string[])definition.anchorNodeIds.Clone();
            }
        }

        /// <summary>
        /// 正负提交声明的现实供给并集（显示与准备动作输入）。只读取目录数据；集合校验仍由 01A
        /// <see cref="CharterSiteInteractionRuntime.PrepareRealitySupplies"/> 唯一执行。
        /// </summary>
        public string[] DeclaredSupplyIds
        {
            get
            {
                CharterRuleDefinitionData definition = Definition;
                if (definition == null || staticCatalog == null || staticCatalog.ReferenceCatalog == null)
                    return Array.Empty<string>();
                var declared = new List<string>();
                foreach (string commitId in new[] { definition.positiveCommit, definition.negativeCommit })
                {
                    CharterCommitReference commit = staticCatalog.ReferenceCatalog.FindCommit(commitId);
                    if (commit == null || commit.realitySupplyIds == null)
                        continue;
                    foreach (string supplyId in commit.realitySupplyIds)
                    {
                        if (!string.IsNullOrWhiteSpace(supplyId) && !declared.Contains(supplyId))
                            declared.Add(supplyId);
                    }
                }
                return declared.ToArray();
            }
        }

        private CharterAuthorityRequirement FindAuthority()
        {
            CharterRuleDefinitionData definition = Definition;
            if (definition == null || staticCatalog == null || staticCatalog.ReferenceCatalog == null)
                return null;
            return staticCatalog.ReferenceCatalog.FindAuthority(definition.requiredAuthority);
        }

        private bool TryEnsurePreparation(out string reason)
        {
            if (interaction == null)
            {
                preparation = null;
                reason = CharterSiteInteractionReasons.SiteUnavailable;
                return false;
            }
            if (preparation == null && !interaction.TryCreatePreparation(out preparation, out reason))
            {
                preparation = null;
                return false;
            }
            reason = CharterSiteInteractionReasons.Ok;
            return true;
        }

        private CharterInteractionActionResult RecordActionResult(string reason)
        {
            lastEvaluation = null;
            lastReason = reason;
            RefreshView();
            return new CharterInteractionActionResult { Succeeded = reason == CharterSiteInteractionReasons.Ok, Reason = reason };
        }

        private CharterInteractionActionResult RecordActionResult(CharterInteractionActionResult result)
        {
            RecordActionResult(result.Reason);
            return result;
        }

        private CharterRuleInvocationResult RecordEvaluationResult(CharterRuleInvocationResult result)
        {
            lastEvaluation = result;
            lastReason = result.Reason;
            RefreshView();
            return result;
        }

        private void ResetInteraction()
        {
            site = null;
            staticCatalog = null;
            catalog = null;
            session = null;
            interaction = null;
            preparation = null;
            lastEvaluation = null;
        }

        private void RefreshView()
        {
            if (view != null)
                view.Refresh();
        }
    }
}
