using System;
using TianZhang.Entity;
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
        private string trialPresetId;

        private static readonly (string Id, string Name, string Abilities)[] TrialChoices =
        {
            (JindanDemoCatalogData.EarthPresetId, "厚载", "土石显化或重力、演武·破垒、御风飞行"),
            (JindanDemoCatalogData.WaterPresetId, "蓄澜", "涨退潮、蓄力、潮眼、御风飞行"),
            (JindanDemoCatalogData.YangPresetId, "尺度与阳照", "方位定尺、日坛昭明、贯石击、御风飞行"),
            (JindanDemoCatalogData.YinPresetId, "封关与藏真", "门枢封关、朔坛藏真"),
        };

        public void ConfigureStartMode(Action<bool> select)
        {
            ordinaryStartButton?.onClick.AddListener(() => select(false));
            jindanStartButton?.onClick.AddListener(() => select(true));
        }
        public void RefreshStartMode(string presetId)
        {
            trialPresetId = presetId;
            bool trial = !string.IsNullOrEmpty(presetId);
            if (startModeText != null) startModeText.text = trial
                ? "金丹试玩 · 关中起步。选择长明或下列四种构筑。"
                : "已选：普通开局｜按出身创建角色。";
            if (ordinaryStartButton != null) ordinaryStartButton.interactable = trial;
            if (jindanStartButton != null) jindanStartButton.interactable = presetId != JindanDemoCatalogData.PlayerFirePresetId;
        }

        public void Configure(Action<string, string> submit, Action<string> selectOrigin, Action<string> selectTrial = null)
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
                var trialId = TrialChoices[index].Id;
                button.onClick.AddListener(() =>
                {
                    if (trialPresetId != null && selectTrial != null) selectTrial(trialId);
                    else selectOrigin(id);
                });
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
            if (slotIdInput != null && (panel?.activeSelf != true || string.IsNullOrWhiteSpace(slotIdInput.text)))
                slotIdInput.text = suggestedSlotId;
            panel?.SetActive(true);
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
                {
                    var choice = TrialChoices[index];
                    originLabels[index].text = trialPresetId == null
                        ? (selected ? "已选 · " : string.Empty) + option.DisplayName
                        : (choice.Id == trialPresetId ? "已选 · " : string.Empty) + choice.Name;
                }
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
            if (trialPresetId != null && summaryText != null)
            {
                var choice = Array.Find(TrialChoices, item => item.Id == trialPresetId);
                bool dual = trialPresetId == JindanDemoCatalogData.YangPresetId || trialPresetId == JindanDemoCatalogData.YinPresetId;
                summaryText.text = (choice.Name ?? "长明") + " · " + (dual ? "金丹温养" : "金丹初结") + " · 秋水游心经\n" +
                    (choice.Abilities ?? "长明火域、有限余火") + "\n本人另有徒手与飞星指，术法槽 5／普通神通槽 2。";
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
