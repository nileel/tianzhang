using System;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CharacterCreation
{
    public sealed class CharacterCreationView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private InputField slotIdInput;
        [SerializeField] private InputField characterNameInput;
        [SerializeField] private Text summaryText;
        [SerializeField] private Text failureText;
        [SerializeField] private Button createButton;
        [SerializeField] private Button returnToMenuButton;
        [SerializeField] private Button[] originButtons;
        [SerializeField] private Text[] originLabels;

        public void Configure(Action<string, string> submit, Action<string> selectOrigin)
        {
            if (submit == null) throw new ArgumentNullException(nameof(submit));
            if (selectOrigin == null) throw new ArgumentNullException(nameof(selectOrigin));
            if (createButton != null)
            {
                createButton.onClick.RemoveAllListeners();
                createButton.onClick.AddListener(() => submit(
                    slotIdInput == null ? string.Empty : slotIdInput.text,
                    characterNameInput == null ? string.Empty : characterNameInput.text));
            }
            for (int index = 0; index < CharacterCreationCatalog.Origins.Length; index++)
            {
                string id = CharacterCreationCatalog.Origins[index].Id;
                Button button = GetOriginButton(id);
                if (button == null) continue;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => selectOrigin(id));
            }
        }

        public void ConfigureReturnToMenu(Action returnToMenu)
        {
            if (returnToMenu == null) throw new ArgumentNullException(nameof(returnToMenu));
            if (returnToMenuButton != null)
            {
                returnToMenuButton.onClick.RemoveAllListeners();
                returnToMenuButton.onClick.AddListener(() => returnToMenu());
            }
        }

        public void Show(CharacterCreationDraft draft)
        {
            panel?.SetActive(true);
            if (slotIdInput != null && string.IsNullOrWhiteSpace(slotIdInput.text)) slotIdInput.text = "slot1";
            if (characterNameInput != null && string.IsNullOrWhiteSpace(characterNameInput.text))
                characterNameInput.text = draft.CharacterName;
            if (failureText != null) failureText.text = string.Empty;
        }

        public Button GetOriginButton(string originId)
        {
            for (int index = 0; index < CharacterCreationCatalog.Origins.Length; index++)
                if (CharacterCreationCatalog.Origins[index].Id == originId)
                    return originButtons != null && index < originButtons.Length ? originButtons[index] : null;
            return null;
        }

        public void RefreshOrigin(CharacterCreationDraft draft, CharacterCreationValidationResult validation)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (validation == null) throw new ArgumentNullException(nameof(validation));
            for (int index = 0; index < CharacterCreationCatalog.Origins.Length; index++)
            {
                OriginOption option = CharacterCreationCatalog.Origins[index];
                bool selected = option.Id == draft.OriginId;
                if (originLabels != null && index < originLabels.Length && originLabels[index] != null)
                    originLabels[index].text = (selected ? "已选 · " : string.Empty) + option.DisplayName;
            }
            OriginOption origin = CharacterCreationCatalog.FindOrigin(draft.OriginId);
            if (summaryText != null)
            {
                string start = origin == null ? "未选择" : DisplayStartNode(origin.StartNodeId);
                summaryText.text = $"出身：{origin?.DisplayName ?? "未选择"}　起点：{start}\n" +
                    $"创建预算：{validation.BudgetUsed}/{validation.BudgetLimit}　返还：{validation.BudgetRefunded}　剩余：{validation.BudgetAvailable}\n" +
                    "属性与灵根沿用当前草稿；进入游戏后再加入门派。";
                if (origin?.Id == "origin_minor_clan" && origin.StartNodeId == "guanzhong_hub")
                    summaryText.text += "\n关中小族试玩：练气起步，可使用贯石击与聚灵术；初始铁剑、铁刀各一件，活血丹三颗。仅新建角色生效，已有存档不变。";
            }
            if (failureText != null) failureText.text = string.Join("\n", validation.Errors);
        }

        private static string DisplayStartNode(string nodeId)
        {
            switch (nodeId)
            {
                case "jiangzuo_hub": return "江左";
                case "guanzhong_hub": return "关中";
                default: return nodeId;
            }
        }

        public void Hide()
        {
            panel?.SetActive(false);
        }

        public void ShowFailure(string reason)
        {
            if (failureText != null) failureText.text = reason;
        }
    }
}
