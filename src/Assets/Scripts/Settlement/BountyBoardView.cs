using System.Collections.Generic;
using System.Text;
using TianZhang.Content;
using TianZhang.Game;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Settlement
{
    /// <summary>
    /// 悬赏面板视图：只枚举当前正式据点由 <see cref="ContentCatalogData.GetBountiesByIssuer"/>
    /// 返回的悬赏，把接取／领奖请求原样交给当前 <see cref="GameSession"/>，并在每次请求后从
    /// <see cref="GameSession.GetBountyState"/> 刷新显示。不判断悬赏规则、不拥有实例状态，
    /// 不伪造目标、奖励、成功日志、状态转换或默认条目。
    /// </summary>
    public sealed class BountyBoardView : MonoBehaviour
    {
        public const string BoardNoBountiesReason = "bounty_board_no_bounties";
        public const string BoardCatalogMissingReason = "bounty_board_catalog_missing";
        public const string BoardSessionMissingReason = "bounty_board_session_missing";
        public const string BoardAcceptSucceededReason = "bounty_accept_ok";
        public const string BoardClaimSucceededReason = "bounty_claim_ok";

        [SerializeField] private Text titleText;
        [SerializeField] private Text entriesText;
        [SerializeField] private Text resultText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button claimButton;
        [SerializeField] private Button closeButton;

        private ContentCatalogData catalog;
        private GameSession session;
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

        public void Show(ContentCatalogData nextCatalog, string nextSettlementId, GameSession nextSession)
        {
            catalog = nextCatalog;
            settlementId = nextSettlementId;
            session = nextSession;
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
            if (session == null)
            {
                SetResult(BoardSessionMissingReason);
                return;
            }

            BountyActionResult result = session.AcceptBounty(catalog, bountyId);
            SetResult(result.Succeeded
                ? BoardAcceptSucceededReason + ":" + bountyId
                : result.FailureReason);
            Refresh();
        }

        public void SubmitClaim(string bountyId)
        {
            if (session == null)
            {
                SetResult(BoardSessionMissingReason);
                return;
            }

            BountyActionResult result = session.ClaimBounty(catalog, bountyId);
            SetResult(result.Succeeded
                ? BoardClaimSucceededReason + ":" + bountyId
                : result.FailureReason);
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

            if (session == null)
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
                BountyStateSnapshot state = session.GetBountyState(bounty.bountyId);
                if (rows.Length > 0)
                    rows.Append('\n');
                rows.Append(bounty.bountyId)
                    .Append(" | ").Append(state.Status)
                    .Append(" | ").Append(state.Progress).Append('/').Append(bounty.requiredCount);
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
            lastResultReason = reason;
            if (resultText != null)
                resultText.text = reason;
        }
    }
}
