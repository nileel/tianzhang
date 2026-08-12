using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Map;
using TianZhang.Settlement;
using TianZhang.Tactical;
using TianZhang.World;

namespace TianZhang.Adventure
{
    public enum AdventureSceneState
    {
        Loading,
        Exploration,
        Combat,
        Returning,
    }

    public class AdventureSceneController : MonoBehaviour
    {
        [SerializeField] private ContentCatalogData contentCatalog;
        [SerializeField] private EnvironmentProfileAsset guanzhongWildEnvironmentProfile;
        [SerializeField] private TextAsset languageTable = null;

        public AdventureSceneState CurrentState { get; private set; } = AdventureSceneState.Loading;
        public CombatSessionOutcome LastEncounterOutcome { get; private set; } = CombatSessionOutcome.Ongoing;
        public FormalEncounterResult LastFormalEncounterResult { get; private set; }
        public string EncounterResolutionFailureReason { get; private set; }
        public string CurrentAdventureId =>
            GameBootstrap.RequireRuntime().Navigation.AdventureId ?? "prototype_adventure";
        public bool RequiresFormalEncounter =>
            CurrentAdventureId == FormalEncounterRules.GuanzhongWildAdventureId;

        private Text adventureIdText;
        private Text sourceText;
        private Text environmentFeedbackText;
        private Button returnToSourceButton;
        private ExplorationController explorationController;
        private string encounterConfigurationError;
        private EnvironmentPresentationSnapshot environmentPresentation;
        private IFormalEncounterRandomSource encounterRandomSource =
            new SystemFormalEncounterRandomSource();
        private bool formalEncounterConsumed;
        private CharterEnvironmentProjectionResult charterEnvironmentProjection;

        private void Awake()
        {
            UiText.Load(languageTable);
            ConfigureCurrentAdventureEncounter();
        }

        private void Start()
        {
            // 编辑态直接调用 Start 的测试也保证语言表已加载（幂等；首个来源生效）。
            UiText.Load(languageTable);
            BuildAdventureUi();
            RefreshAdventureUi();
            if (string.IsNullOrEmpty(encounterConfigurationError))
                MarkExplorationReady();
            Debug.Log("[AdventureScene] started");
        }

        public void MarkExplorationReady()
        {
            if (string.IsNullOrEmpty(encounterConfigurationError) && CurrentState != AdventureSceneState.Returning)
                CurrentState = AdventureSceneState.Exploration;
        }

        public void BeginEncounter()
        {
            if (string.IsNullOrEmpty(encounterConfigurationError) && CurrentState != AdventureSceneState.Returning)
                CurrentState = AdventureSceneState.Combat;
        }

        public void CompleteEncounter()
        {
            if (CurrentState == AdventureSceneState.Combat)
                CurrentState = AdventureSceneState.Exploration;
        }

        public void ResolveEncounterAndReturn(CombatSessionOutcome outcome)
        {
            ResolveEncounterAndReturn(outcome, null);
        }

        public void ResolveEncounterAndReturn(
            CombatSessionOutcome outcome,
            EnemyData defeatedEnemy)
        {
            if (outcome != CombatSessionOutcome.Victory && outcome != CombatSessionOutcome.Defeat)
                throw new System.ArgumentOutOfRangeException(nameof(outcome), outcome, "Only completed encounter outcomes may return to the source scene.");

            LastEncounterOutcome = outcome;
            if (RequiresFormalEncounter)
                ConsumeFormalEncounterResult(outcome, defeatedEnemy);

            MarkReturning();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.ReturnToPreviousScene();
        }

        public void MarkReturning()
        {
            CurrentState = AdventureSceneState.Returning;
        }

        public void ReturnToSource()
        {
            MarkReturning();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.ReturnToPreviousScene();
        }

        public void ReturnToWorld()
        {
            MarkReturning();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(
                    GameBootstrap.RequireRuntime().Navigation.WorldNodeId);
        }

        public string BuildSourceDescription()
        {
            SceneReturnTarget target = GameBootstrap.RequireRuntime().Navigation.ReturnTarget;
            if (target.SceneName == GameplaySceneNames.Settlement)
                return "来源据点: " + (string.IsNullOrEmpty(target.SettlementId)
                    ? "未记录"
                    : UiText.ResolveId("settlement_", target.SettlementId));

            if (target.SceneName == GameplaySceneNames.World)
                return "来源主世界: " + (string.IsNullOrEmpty(target.WorldNodeId)
                    ? "未记录"
                    : WorldSceneController.NodeDisplayName(target.WorldNodeId));

            return "来源: 未记录";
        }

        public void SetContentCatalog(ContentCatalogData catalog)
        {
            contentCatalog = catalog;
        }

        public void SetGuanzhongWildEnvironmentProfile(EnvironmentProfileAsset environmentProfile)
        {
            guanzhongWildEnvironmentProfile = environmentProfile;
        }

        public void SetEncounterRandomSource(IFormalEncounterRandomSource randomSource)
        {
            encounterRandomSource = randomSource;
        }

        private void ConfigureCurrentAdventureEncounter()
        {
            encounterConfigurationError = null;
            EncounterResolutionFailureReason = null;
            LastFormalEncounterResult = null;
            formalEncounterConsumed = false;
            charterEnvironmentProjection = null;
            if (!RequiresFormalEncounter)
                return;

            explorationController = FindFirstObjectByType<ExplorationController>();
            if (explorationController == null)
            {
                BlockGuanzhongWildEncounter("guanzhong_wild 缺少正式探索控制器，已阻止遭遇启动。");
                return;
            }

            explorationController.ClearFormalEncounter();
            if (!FormalEncounterRules.TryResolveGuanzhongEnemy(
                    contentCatalog,
                    out EnemyData enemy,
                    out ICombatActionPolicy aiPolicy,
                    out string enemyReason))
            {
                BlockGuanzhongWildEncounter(
                    FormalEncounterRules.GuanzhongWildAdventureId +
                    " 正式敌人配置被拒绝: " + enemyReason);
                return;
            }

            if (guanzhongWildEnvironmentProfile == null ||
                guanzhongWildEnvironmentProfile.profileId != "env_guanzhong_wild")
            {
                BlockGuanzhongWildEncounter("guanzhong_wild 必须显式绑定 env_guanzhong_wild 环境档案，已阻止遭遇启动。");
                return;
            }

            // 投影结果无论成功或失败都只用于显示：成功显示条目事件／档案 ID，失败显示稳定原因；
            // 环境链仍由上方既有绑定门禁与 U-ENV-RULE-01B 表现链唯一消费，投影不阻断遭遇启动。
            CharterEnvironmentProjection.TryResolve(
                GameBootstrap.RequireRuntime().Charters.CurrentState,
                contentCatalog,
                guanzhongWildEnvironmentProfile.profileId,
                out CharterEnvironmentProjectionResult environmentProjection);
            charterEnvironmentProjection = environmentProjection;

            explorationController.ConfigureFormalEncounter(enemy, aiPolicy);
            explorationController.ConfigureEnvironmentProfile(guanzhongWildEnvironmentProfile);
            explorationController.enabled = true;
            explorationController.enemyCount = 1;
            explorationController.enemyTemplates = System.Array.Empty<CharacterData>();
        }

        private void BlockGuanzhongWildEncounter(string error)
        {
            encounterConfigurationError = error;
            CurrentState = AdventureSceneState.Loading;
            if (explorationController != null)
                explorationController.enabled = false;
            Debug.LogError("[AdventureScene] " + error);
        }

        public void ReportEncounterConfigurationFailure(string error)
        {
            BlockGuanzhongWildEncounter(error);
        }

        private void ConsumeFormalEncounterResult(
            CombatSessionOutcome outcome,
            EnemyData defeatedEnemy)
        {
            if (formalEncounterConsumed)
            {
                RecordEncounterResolutionFailure(FormalEncounterRules.AlreadyConsumedReason);
                return;
            }

            formalEncounterConsumed = true;
            if (!FormalEncounterResult.TryCreate(
                    contentCatalog,
                    defeatedEnemy,
                    CurrentAdventureId,
                    outcome,
                    encounterRandomSource,
                    out FormalEncounterResult result,
                    out string resultReason))
            {
                RecordEncounterResolutionFailure(resultReason);
                return;
            }

            LastFormalEncounterResult = result;
            if (outcome != CombatSessionOutcome.Victory)
                return;

            GameRuntime runtime = GameBootstrap.RequireRuntime();
            runtime.Bounties.RecordDefeat(contentCatalog, result.AdventureId, result.EnemyId);

            if (result.DropGrants.Count == 0)
                return;

            var requests = new List<TianZhang.World.InventoryGrantRequest>(result.DropGrants.Count);
            foreach (FormalDropGrant grant in result.DropGrants)
                requests.Add(new TianZhang.World.InventoryGrantRequest(grant.ItemId, grant.Quantity));

            TianZhang.World.InventoryGrantResult grantResult =
                runtime.InventoryGrants.Grant(contentCatalog, requests);
            if (!grantResult.Applied)
            {
                RecordEncounterResolutionFailure(
                    "formal_encounter_inventory_grant_failed:" +
                    grantResult.FailureReason);
            }
        }

        private void RecordEncounterResolutionFailure(string reason)
        {
            EncounterResolutionFailureReason = reason;
            Debug.LogError("[AdventureScene] 正式遭遇结算失败: " + reason);
            RefreshAdventureUi();
        }

        public void SetEnvironmentPresentation(EnvironmentPresentationSnapshot presentation)
        {
            environmentPresentation = presentation;
            RefreshAdventureUi();
        }

        private void BuildAdventureUi()
        {
            if (GameObject.Find("AdventurePanel") != null)
            {
                adventureIdText = GameObject.Find("AdventureIdText")?.GetComponent<Text>();
                sourceText = GameObject.Find("AdventureSourceText")?.GetComponent<Text>();
                environmentFeedbackText = GameObject.Find("EnvironmentFeedbackText")?.GetComponent<Text>();
                returnToSourceButton = GameObject.Find("ReturnToSourceButton")?.GetComponent<Button>();
                return;
            }

            var canvas = EnsureUICanvas();

            var panelGo = new GameObject("AdventurePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(canvas.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 1f);
            const float panelMargin = 24f;
            var playerPanel = canvas.transform.Find("PlayerPanel") as RectTransform;
            float panelTop = playerPanel != null ? playerPanel.offsetMin.y - panelMargin : -panelMargin;
            panelRt.anchoredPosition = new Vector2(panelMargin, panelTop);
            panelRt.sizeDelta = new Vector2(380f, 390f);
            panelGo.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.08f, 0.9f);
            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 18);
            layout.spacing = 10f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateText("AdventureTitle", panelGo.transform, "副本", 26, Color.white, TextAnchor.MiddleCenter, 38f);
            adventureIdText = CreateText("AdventureIdText", panelGo.transform, "", 18, Color.yellow, TextAnchor.MiddleCenter, 32f).GetComponent<Text>();
            sourceText = CreateText("AdventureSourceText", panelGo.transform, "", 15, new Color(0.85f, 0.85f, 0.78f), TextAnchor.MiddleCenter, 42f).GetComponent<Text>();
            environmentFeedbackText = CreateText("EnvironmentFeedbackText", panelGo.transform, "", 14, new Color(0.76f, 0.9f, 0.82f), TextAnchor.UpperLeft, 170f).GetComponent<Text>();

            returnToSourceButton = CreateButton("ReturnToSourceButton", panelGo.transform, "返回来源", new Color(0.28f, 0.34f, 0.42f, 1f)).GetComponent<Button>();
            returnToSourceButton.onClick.AddListener(ReturnToSource);
        }

        private void RefreshAdventureUi()
        {
            if (adventureIdText != null)
                adventureIdText.text = "当前副本: " + GetAdventureDisplayName();

            if (sourceText != null)
            {
                string encounterError = string.IsNullOrEmpty(encounterConfigurationError)
                    ? EncounterResolutionFailureReason
                    : encounterConfigurationError;
                // 错误文本中的稳定原因只做显示映射；原始原因保留在字段与 Debug 日志中。
                sourceText.text = string.IsNullOrEmpty(encounterError)
                    ? BuildSourceDescription()
                    : UiText.ResolveEmbedded(encounterError) + "\n" + BuildSourceDescription();
            }

            if (environmentFeedbackText != null)
                environmentFeedbackText.text = BuildEnvironmentFeedbackDescription();

            if (returnToSourceButton != null)
                returnToSourceButton.interactable = SceneFlowManager.Instance != null;
        }

        private string GetAdventureDisplayName()
        {
            return UiText.ResolveId("adventure_", CurrentAdventureId);
        }

        private string BuildEnvironmentFeedbackDescription()
        {
            var lines = new List<string>();
            if (charterEnvironmentProjection != null)
            {
                lines.Add(charterEnvironmentProjection.Succeeded
                    ? "册界环境引用: 已生效（条目: " + JoinDisplayNames(charterEnvironmentProjection.RuleEntryIds) + "）"
                    : UiText.ReasonDisplay(charterEnvironmentProjection.Reason, "册界环境引用未生效"));
            }

            if (environmentPresentation == null)
                return string.Join("\n", lines);
            if (!environmentPresentation.IsConfigured)
            {
                // 稳定原因保留在投影对象与开发日志；玩家只看到可理解的失败。
                Debug.LogWarning("[AdventureScene] 环境反馈未生效: " + environmentPresentation.FailureReason);
                lines.Add("环境反馈: 未生效");
                return string.Join("\n", lines);
            }

            var channels = new List<string>();
            foreach (var channel in environmentPresentation.PhenomenonChannels)
                channels.Add(GetPhenomenonChannelDisplayName(channel));

            var edges = new List<string>();
            foreach (var edge in environmentPresentation.DirectedEdges)
            {
                edges.Add(
                    "(" + edge.From.q + "," + edge.From.r + ")→(" + edge.To.q + "," + edge.To.r + ") " +
                    "移动" + FormatInteractionState(edge.MovementAllowed, edge.MovementReason) + "，交互" +
                    FormatInteractionState(edge.InteractionAllowed, edge.InteractionReason));
            }

            lines.Add("地表: " + JoinDisplayNames(environmentPresentation.SurfacePrototypeRefs));
            lines.Add("现象通道: " + string.Join("、", channels));
            lines.Add("格边: " + string.Join("；", edges));
            return string.Join("\n", lines);
        }

        /// <summary>生产实体显示名解析；无键的引用不是已批准显示事实，按数量概括并保留开发日志。</summary>
        private static string JoinDisplayNames(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0)
                return "无";

            var names = new List<string>(ids.Count);
            foreach (string id in ids)
            {
                string resolved = UiText.Resolve(id);
                if (string.Equals(resolved, id, StringComparison.Ordinal))
                    Debug.LogWarning("[AdventureScene] 无批准显示事实的引用（保留原始值）: " + id);
                names.Add(resolved);
            }

            return string.Join("、", names);
        }

        private static string FormatInteractionState(bool allowed, string reason)
        {
            if (allowed)
                return "允许";
            return "不允许（" + UiText.ReasonDisplay(reason, "受限") + "）";
        }

        private static string GetPhenomenonChannelDisplayName(EnvironmentPhenomenonChannel channel)
        {
            switch (channel)
            {
                case EnvironmentPhenomenonChannel.Airflow:
                    return "气流";
                case EnvironmentPhenomenonChannel.Visibility:
                    return "能见度";
                case EnvironmentPhenomenonChannel.Temperature:
                    return "温度";
                case EnvironmentPhenomenonChannel.Precipitation:
                    return "降水";
                case EnvironmentPhenomenonChannel.SuspendedHazard:
                    return "悬浮危害";
                case EnvironmentPhenomenonChannel.CloudDischarge:
                    return "云气／放电";
                default:
                    return channel.ToString();
            }
        }

        private static GameObject EnsureUICanvas()
        {
            var canvasGo = GameObject.Find("UICanvas");
            if (canvasGo != null)
                return canvasGo;

            canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            return canvasGo;
        }

        private static GameObject CreateText(string name, Transform parent, string text, int fontSize, Color color, TextAnchor anchor, float preferredHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = preferredHeight;
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string labelText, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<LayoutElement>().preferredHeight = 42f;

            var label = CreateText("Label", go.transform, labelText, 16, Color.white, TextAnchor.MiddleCenter, 42f);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            return go;
        }
    }
}
