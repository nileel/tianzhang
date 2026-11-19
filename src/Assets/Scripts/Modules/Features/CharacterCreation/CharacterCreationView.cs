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

        [SerializeField] private Button ordinaryStartButton;
        [SerializeField] private Button jindanStartButton;
        [SerializeField] private Text startModeText;
        private bool trialStart;

        public void ConfigureStartMode(Action<bool> select)
        {
            ordinaryStartButton?.onClick.AddListener(() => select(false));
            jindanStartButton?.onClick.AddListener(() => select(true));
        }
        public void RefreshStartMode(bool trial)
        {
            trialStart = trial;
            if (startModeText != null) startModeText.text = trial
                ? "已选：金丹试玩·长明｜关中开局，固定火灵根与合法来源；徒手、飞星指与长明同属本人。"
                : "已选：普通开局｜按出身创建角色。";
            if (ordinaryStartButton != null) ordinaryStartButton.interactable = trial;
            if (jindanStartButton != null) jindanStartButton.interactable = !trial;
            foreach (var button in originButtons ?? Array.Empty<Button>()) button.interactable = !trial;
        }

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

        public void Show(CharacterCreationDraft draft, string suggestedSlotId)
        {
            panel?.SetActive(true);
            if (slotIdInput != null) slotIdInput.text = suggestedSlotId;
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
            if (trialStart && summaryText != null)
                summaryText.text = "金丹初结 · 秋水游心经 · 火界长明\n" +
                    "术法槽 5／普通神通槽 2；长明占独立界席位，无飞行。\n" +
                    "新建独立试玩存档，保留姓名与装配；挑战内状态结束后清空。";
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
