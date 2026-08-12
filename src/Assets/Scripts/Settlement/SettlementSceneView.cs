using System;
using System.Collections.Generic;
using TianZhang.Content;
using TianZhang.Game;
using TianZhang.World;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Settlement
{
    /// <summary>
    /// 共享玩家显示文本解析（U-GZ-UI-TEXT-01）：唯一文本源是 <c>Assets/DataConfig/Language.csv</c>
    /// （场景构建时序列化为 TextAsset 引用），视图与表现边界只做单向显示映射，不修改业务状态、
    /// 稳定 ID 或稳定原因字段。加载幂等：首个来源生效；未加载时 <see cref="Resolve"/> 原样返回键。
    /// </summary>
    public static class UiText
    {
        /// <summary>
        /// 个别大写稳定原因（既有协议遗留，不符合 Language.csv 小写 ASCII 键约束）的显示覆盖；
        /// 只覆盖显示，原始原因仍保留在结果对象与 LastReason 字段。
        /// </summary>
        private static readonly Dictionary<string, string> ReasonOverrides =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "TZ_CHARTER_CONFLICT_YUANYING_ANCHORED", "元婴受锚成功" },
            };

        private static readonly Dictionary<string, string> Table =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static string[] sortedKeys = Array.Empty<string>();
        private static bool loaded;

        public static bool IsLoaded => loaded;

        /// <summary>只接受首个来源；同一进程内所有场景共享同一 Language 表。</summary>
        public static void Load(TextAsset source)
        {
            if (loaded || source == null)
                return;
            LoadText(source.text);
        }

        public static void LoadText(string csvText)
        {
            if (loaded || string.IsNullOrEmpty(csvText))
                return;

            foreach (string rawLine in csvText.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                int comma = line.IndexOf(',');
                if (comma <= 0)
                    continue;
                string key = line.Substring(0, comma).Trim();
                if (key.Length == 0)
                    continue;
                Table[key] = line.Substring(comma + 1).Trim();
            }

            sortedKeys = new string[Table.Count];
            Table.Keys.CopyTo(sortedKeys, 0);
            Array.Sort(sortedKeys, (left, right) => right.Length.CompareTo(left.Length));
            loaded = true;
        }

        public static bool TryResolve(string key, out string text)
        {
            if (!string.IsNullOrEmpty(key) && Table.TryGetValue(key, out text))
                return true;
            text = null;
            return false;
        }

        /// <summary>Language 键 → 中文；键缺失时原样返回键本身（不伪造文本）。</summary>
        public static string Resolve(string key)
        {
            return TryResolve(key, out string text) ? text : key;
        }

        /// <summary>按命名前缀解析稳定 ID（如 "settlement_" + "guanzhong_city"）；无键时回退原始 ID。</summary>
        public static string ResolveId(string keyPrefix, string id)
        {
            string key = keyPrefix + id;
            return TryResolve(key, out string text) ? text : id;
        }

        /// <summary>
        /// 稳定原因 → 玩家文本：带 ":后缀" 的原因取前缀映射，后缀中的稳定 ID 不属于显示事实；
        /// 未知原因显示 <paramref name="unknownFallback"/> 并把原始原因保留到开发日志，不伪造成功。
        /// </summary>
        public static string ReasonDisplay(string reason, string unknownFallback)
        {
            if (string.IsNullOrEmpty(reason))
                return string.Empty;

            string key = reason;
            int colon = reason.IndexOf(':');
            if (colon > 0)
                key = reason.Substring(0, colon);
            if (ReasonOverrides.TryGetValue(key, out string overrideText))
                return overrideText;
            if (TryResolve(key, out string text))
                return text;

            Debug.LogWarning("[UiText] 未映射的稳定原因（保留原始值）: " + reason);
            return unknownFallback ?? string.Empty;
        }

        /// <summary>
        /// 复合文本（遭遇错误等已证明显示字段）边界的单向解析：把嵌入的已知 Language 键替换为中文，
        /// 其余原样保留。只允许用于内容已证明为机器原因/键的显示字段（如 AdventureSourceText 的
        /// 遭遇错误），不得用于任意整条战斗日志或用户文本（会误改自定义名称/技术日志）。
        /// </summary>
        public static string ResolveEmbedded(string text)
        {
            if (string.IsNullOrEmpty(text) || !loaded)
                return text;

            string result = text;
            foreach (string key in sortedKeys)
                result = result.Replace(key, Table[key]);
            return result;
        }
    }

    public sealed class SettlementSceneView : MonoBehaviour
    {
        [SerializeField] private TextAsset languageTable = null;
        [SerializeField] private Text settlementNameText;
        [SerializeField] private Text settlementDetailText;
        [SerializeField] private Text settlementStatusText;
        [SerializeField] private Button featureButton;
        [SerializeField] private Text featureButtonText;
        [SerializeField] private Button adventureButton;
        [SerializeField] private Text adventureButtonText;
        [SerializeField] private Button returnToWorldButton;
        [SerializeField] private BountyBoardView bountyBoardView;
        [SerializeField] private Button charterSiteEntryButton;
        [SerializeField] private Text charterSiteEntryText;
        [SerializeField] private CharterSiteView charterSiteView;

        private void Start()
        {
            UiText.Load(languageTable);
        }

        /// <summary>编辑态直接调用视图方法时也保证表已加载（幂等；首个来源生效）。</summary>
        private void EnsureLanguageLoaded()
        {
            UiText.Load(languageTable);
        }

        public void Configure(
            Text nameText,
            Text detailText,
            Text statusText,
            Button nextFeatureButton,
            Text nextFeatureButtonText,
            Button nextAdventureButton,
            Text nextAdventureButtonText,
            Button nextReturnToWorldButton,
            BountyBoardView nextBountyBoard,
            Button nextCharterSiteEntryButton,
            Text nextCharterSiteEntryText,
            CharterSiteView nextCharterSiteView)
        {
            settlementNameText = nameText;
            settlementDetailText = detailText;
            settlementStatusText = statusText;
            featureButton = nextFeatureButton;
            featureButtonText = nextFeatureButtonText;
            adventureButton = nextAdventureButton;
            adventureButtonText = nextAdventureButtonText;
            returnToWorldButton = nextReturnToWorldButton;
            bountyBoardView = nextBountyBoard;
            charterSiteEntryButton = nextCharterSiteEntryButton;
            charterSiteEntryText = nextCharterSiteEntryText;
            charterSiteView = nextCharterSiteView;
        }

        public bool HasCharterSitePanel => charterSiteView != null;

        public void BindCharterSiteEntry(Action onClick)
        {
            if (charterSiteEntryButton == null)
                return;

            charterSiteEntryButton.onClick.RemoveAllListeners();
            if (onClick != null)
                charterSiteEntryButton.onClick.AddListener(() => onClick());
        }

        public void SetCharterSiteEntryText(string value)
        {
            EnsureLanguageLoaded();
            if (charterSiteEntryText != null)
                charterSiteEntryText.text = "旧水驿入口: " + UiText.ReasonDisplay(value, "不可用");
        }

        /// <summary>
        /// 打开唯一站点面板：只由 Settlement 控制器在目录取得站点、静态目录和会话引用都合法时调用。
        /// </summary>
        public bool OpenCharterSite(
            CharterSiteData site,
            CharterRuleStaticCatalogData staticCatalog,
            ContentCatalogData catalog,
            CharterUseCase useCase,
            string currentSettlementId,
            out string reason)
        {
            if (charterSiteView == null)
            {
                reason = SettlementSceneController.CharterSitePanelMissingReason;
                return false;
            }

            return charterSiteView.Show(site, staticCatalog, catalog, useCase, currentSettlementId, out reason);
        }

        public void SetReturnToWorldAction(Action action)
        {
            if (returnToWorldButton == null)
                return;

            returnToWorldButton.onClick.RemoveAllListeners();
            if (action != null)
                returnToWorldButton.onClick.AddListener(() => action());
        }

        public void ShowSettlement(SettlementData settlement, string returnWorldNodeId)
        {
            EnsureLanguageLoaded();
            if (settlementNameText != null)
                settlementNameText.text = UiText.Resolve(settlement.displayNameKey);
            if (settlementDetailText != null)
            {
                settlementDetailText.text =
                    "据点: " + UiText.Resolve(settlement.displayNameKey) +
                    "\n区域: " + UiText.Resolve("region_" + settlement.regionId);
            }

            SetStatus("据点已加载 · 返回世界：" + WorldNodeDisplay(returnWorldNodeId));
        }

        public void ShowFailure(string reason, string returnWorldNodeId)
        {
            EnsureLanguageLoaded();
            if (settlementNameText != null)
                settlementNameText.text = "据点不可用";
            if (settlementDetailText != null)
                settlementDetailText.text = "返回主世界：" + WorldNodeDisplay(returnWorldNodeId);
            SetStatus(UiText.ReasonDisplay(reason, "据点不可用"));
            SetButton(featureButton, featureButtonText, "功能不可用", false, null);
            SetButton(adventureButton, adventureButtonText, "副本不可用", false, null);
        }

        public void BindFeature(SettlementFeatureData feature, Action<SettlementFeatureData> onClick)
        {
            EnsureLanguageLoaded();
            if (feature == null)
            {
                ShowFeatureResult("settlement_feature_missing");
                SetButton(featureButton, featureButtonText, "功能不可用", false, null);
                return;
            }

            bool enabled = string.Equals(feature.availability, "enabled", StringComparison.Ordinal);
            SetButton(featureButton, featureButtonText, UiText.Resolve(feature.displayNameKey), enabled, () => onClick?.Invoke(feature));
            if (!enabled)
                ShowFeatureResult(SettlementFeatureDispatcher.FeatureDisabledReason + ":" + feature.disabledReasonKey);
        }

        public void BindAdventure(string adventureId, Action<string> onClick)
        {
            EnsureLanguageLoaded();
            bool available = !string.IsNullOrWhiteSpace(adventureId);
            SetButton(
                adventureButton,
                adventureButtonText,
                available ? "进入副本: " + UiText.ResolveId("adventure_", adventureId) : "副本不可用",
                available,
                () => onClick?.Invoke(adventureId));
        }

        public void ShowFeatureResult(string reason)
        {
            EnsureLanguageLoaded();
            SetStatus(UiText.ReasonDisplay(reason, "功能不可用"));
        }

        public void ShowAdventureResult(string reason)
        {
            EnsureLanguageLoaded();
            SetStatus(UiText.ReasonDisplay(reason, "副本不可用"));
        }

        public void OpenBountyBoard(ContentCatalogData catalog, string settlementId, BountyUseCase useCase)
        {
            if (bountyBoardView != null)
                bountyBoardView.Show(catalog, settlementId, useCase);
        }

        private void SetStatus(string value)
        {
            if (settlementStatusText != null)
                settlementStatusText.text = value;
        }

        private static string WorldNodeDisplay(string nodeId)
        {
            return WorldSceneController.NodeDisplayName(nodeId);
        }

        private static void SetButton(Button button, Text label, string text, bool interactable, Action action)
        {
            if (label != null)
                label.text = text;
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            button.interactable = interactable;
            if (action != null)
                button.onClick.AddListener(() => action());
        }
    }
}
