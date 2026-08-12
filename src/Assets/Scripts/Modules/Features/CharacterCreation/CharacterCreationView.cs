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

        public void Configure(Action<string, string> submit)
        {
            if (submit == null) throw new ArgumentNullException(nameof(submit));
            if (createButton == null) return;
            createButton.onClick.RemoveAllListeners();
            createButton.onClick.AddListener(() => submit(
                slotIdInput == null ? string.Empty : slotIdInput.text,
                characterNameInput == null ? string.Empty : characterNameInput.text));
        }

        public void Show(CharacterCreationDraft draft)
        {
            panel?.SetActive(true);
            if (slotIdInput != null && string.IsNullOrWhiteSpace(slotIdInput.text)) slotIdInput.text = "slot1";
            if (characterNameInput != null) characterNameInput.text = draft.CharacterName;
            if (summaryText != null)
                summaryText.text = "创建角色：属性、灵根、出身使用当前批准的默认草稿；进入游戏后再加入门派。";
            if (failureText != null) failureText.text = string.Empty;
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
