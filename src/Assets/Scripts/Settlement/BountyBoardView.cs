using System.Collections.Generic;
using System.Text;
using TianZhang.Content;
using TianZhang.World;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Settlement
{
    /// <summary>
    /// 悬赏面板视图：只枚举当前正式据点由 <see cref="ContentCatalogData.GetBountiesByIssuer"/>
    /// 返回的悬赏，把接取／领奖请求原样交给当前 <see cref="BountyUseCase"/>，并在每次请求后从
    /// <see cref="BountyUseCase.GetState"/> 刷新显示。不判断悬赏规则、不拥有实例状态，
    /// 不伪造目标、奖励、成功日志、状态转换或默认条目。
    /// </summary>
    public sealed class BountyBoardView : MonoBehaviour
    {
        public const string BoardNoBountiesReason = "bounty_board_no_bounties";
        public const string BoardCatalogMissingReason = "bounty_board_catalog_missing";
        public const string BoardSessionMissingReason = "bounty_board_session_missing";

        [SerializeField] private Text titleText;
        [SerializeField] private Text entriesText;
        [SerializeField] private Text resultText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button claimButton;
        [SerializeField] private Button closeButton;

        private ContentCatalogData catalog;
        private BountyUseCase useCase;
        private string settlementId;
        private string currentBountyId;
        private string lastResultReason;
        private readonly List<BountyData> listedBounties = new List<BountyData>();

        public bool IsOpen => gameObject.activeSelf;
        public string CurrentBountyId => currentBountyId;
        public string LastResultReason => lastResultReason;
        public int ListedBountyCount => listedBounties.Count;

        public void Configure(
            Text title,
            Text entries,
            Text result,
            Button accept,
            Button claim,
            Button close)
        {
            titleText = title;
            entriesText = entries;
            resultText = result;
            acceptButton = accept;
            claimButton = claim;
            closeButton = close;
        }

        public void Show(ContentCatalogData nextCatalog, string nextSettlementId, BountyUseCase nextUseCase)
        {
            catalog = nextCatalog;
            settlementId = nextSettlementId;
            useCase = nextUseCase;
            lastResultReason = null;
            BindButtonListeners();
            gameObject.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void BindButtonListeners()
        {
            if (acceptButton != null)
            {
                acceptButton.onClick.RemoveAllListeners();
                acceptButton.onClick.AddListener(AcceptCurrentBounty);
            }

            if (claimButton != null)
            {
                claimButton.onClick.RemoveAllListeners();
                claimButton.onClick.AddListener(ClaimCurrentBounty);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
        }

        public void AcceptCurrentBounty()
        {
            SubmitAccept(currentBountyId);
        }

        public void ClaimCurrentBounty()
        {
            SubmitClaim(currentBountyId);
        }

        public void SubmitAccept(string bountyId)
        {
            if (useCase == null)
            {
                SetResult(BoardSessionMissingReason);
                return;
            }

            BountyActionResult result = useCase.Accept(catalog, bountyId, settlementId);
            // 成功不写入任何结果字面量：成功显示只反映 Refresh 后同一用例状态的实际状态。
            SetResult(result.Succeeded ? null : result.FailureReason);
            Refresh();
        }

        public void SubmitClaim(string bountyId)
        {
            if (useCase == null)
            {
                SetResult(BoardSessionMissingReason);
                return;
            }

            BountyActionResult result = useCase.Claim(catalog, bountyId);
            SetResult(result.Succeeded ? null : result.FailureReason);
            Refresh();
        }

        private void Refresh()
        {
            listedBounties.Clear();
            if (catalog == null)
            {
                currentBountyId = null;
                SetResult(BoardCatalogMissingReason);
                UpdateButtons();
                return;
            }

            if (useCase == null)
            {
                currentBountyId = null;
                SetResult(BoardSessionMissingReason);
                UpdateButtons();
                return;
            }

            var rows = new StringBuilder();
            foreach (BountyData bounty in catalog.GetBountiesByIssuer(settlementId))
            {
                if (bounty == null || string.IsNullOrWhiteSpace(bounty.bountyId))
                    continue;

                listedBounties.Add(bounty);
                BountyState state = useCase.GetState(bounty.bountyId);
                if (rows.Length > 0)
                    rows.Append('\n');
                // 标题优先解析已批准 titleKey；缺键（非正式实体）回退稳定 ID，不伪造占位文本。
                string resolvedTitle = string.IsNullOrWhiteSpace(bounty.titleKey)
                    ? null
                    : UiText.Resolve(bounty.titleKey);
                string title = string.IsNullOrEmpty(resolvedTitle) || resolvedTitle == bounty.titleKey
                    ? bounty.bountyId
                    : resolvedTitle;
                rows.Append(title)
                    .Append(" | ").Append(StatusDisplay(state.Status))
                    .Append(" | 进度 ").Append(state.Progress).Append('/').Append(bounty.requiredCount);
            }

            currentBountyId = listedBounties.Count == 0 ? null : listedBounties[0].bountyId;
            if (entriesText != null)
                entriesText.text = rows.ToString();
            if (listedBounties.Count == 0 && string.IsNullOrEmpty(lastResultReason))
                SetResult(BoardNoBountiesReason);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool hasCurrent = !string.IsNullOrEmpty(currentBountyId);
            if (acceptButton != null)
                acceptButton.interactable = hasCurrent;
            if (claimButton != null)
                claimButton.interactable = hasCurrent;
        }

        private void SetResult(string reason)
        {
            // 内部稳定原因始终保留在 LastResultReason；玩家文本只做单向映射，不吞掉失败。
            lastResultReason = reason;
            if (resultText != null)
                resultText.text = UiText.ReasonDisplay(reason, "操作失败");
        }

        private static string StatusDisplay(BountyStatus status)
        {
            string key;
            switch (status)
            {
                case BountyStatus.Accepted:
                    key = "bounty_status_accepted";
                    break;
                case BountyStatus.ObjectiveCompleted:
                    key = "bounty_status_completed";
                    break;
                case BountyStatus.Claimed:
                    key = "bounty_status_claimed";
                    break;
                default:
                    key = "bounty_status_available";
                    break;
            }

            return UiText.Resolve(key);
        }
    }
}
