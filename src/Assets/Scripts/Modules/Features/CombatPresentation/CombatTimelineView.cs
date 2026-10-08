using System;
using System.Globalization;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatTimelineView : MonoBehaviour
    {
        private const int MaximumEntries = 2;
        private const float EntrySpacing = 5f;

        [SerializeField] private GameObject root;
        [SerializeField] private Text tickText;
        [SerializeField] private Text focusText;
        [SerializeField] private RectTransform[] entryRoots = new RectTransform[MaximumEntries];
        [SerializeField] private Button[] entryButtons = new Button[MaximumEntries];
        [SerializeField] private Text[] entryLabels = new Text[MaximumEntries];
        [SerializeField] private Image[] entryPortraits = new Image[MaximumEntries];
        [SerializeField] private Sprite playerPortrait;

        [SerializeField] private GameObject predictionRoot;
        [SerializeField] private Text predictionText;
        public CombatActionPreviewSnapshot Prediction { get; private set; }

        public void PresentPrediction(CombatActionPreviewSnapshot value)
        {
            Prediction = value != null && value.CanConfirm ? value : null;
            if (predictionRoot != null) predictionRoot.SetActive(Prediction != null);
            if (predictionText != null) predictionText.text = Prediction == null ? string.Empty :
                "◇ 若此动作\n完成并结束\n+" + value.TicksUntilReady + " 刻\n顺位未定";
        }

        private CombatTimelineSnapshot snapshot;
        private string focusedCombatantId;
        private int hoveredSlot = -1;
        private string inspectedTargetId;
        private Action<string> selectTarget;
        public string InspectedTargetId => inspectedTargetId;

        public void ConfigureSelection(Action<string> onSelected) => selectTarget = onSelected;

        public void SetInspectedTarget(string id)
        {
            inspectedTargetId = id;
            if (snapshot == null) return;
            for (int index = 0; index < Math.Min(MaximumEntries, snapshot.Entries.Count); index++)
                if (entryLabels[index] != null)
                    entryLabels[index].text = FormatEntry(snapshot.Entries[index], index);
        }

        public string FocusedCombatantId => focusedCombatantId;

        private void Awake()
        {
            for (int index = 0; index < MaximumEntries; index++)
            {
                Button button = entryButtons[index];
                if (button == null) continue;
                int slot = index;
                button.onClick.AddListener(() =>
                {
                    FocusEntry(slot);
                    if (snapshot != null && slot < snapshot.Entries.Count) selectTarget?.Invoke(snapshot.Entries[slot].Id);
                });
                EventTrigger trigger = button.GetComponent<EventTrigger>();
                if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>();
                AddFocusEvent(trigger, EventTriggerType.PointerEnter, slot);
                AddFocusEvent(trigger, EventTriggerType.Select, slot);
                AddFocusExit(trigger, EventTriggerType.PointerExit);

            }
        }

        public void Present(CombatTimelineSnapshot timeline)
        {
            if (timeline == null)
            {
                Hide();
                return;
            }

            string keyboardFocusId = null;
            if (snapshot != null && EventSystem.current != null)
                for (int index = 0; index < Math.Min(MaximumEntries, snapshot.Entries.Count); index++)
                    if (entryButtons[index] != null && EventSystem.current.currentSelectedGameObject == entryButtons[index].gameObject)
                        keyboardFocusId = snapshot.Entries[index].Id;
            snapshot = timeline;
            if (root != null) root.SetActive(true);
            if (tickText != null) tickText.text = "刻 " + timeline.CurrentTick;
            float top = entryRoots[0] != null ? entryRoots[0].anchoredPosition.y : 0f;
            int count = Math.Min(MaximumEntries, timeline.Entries.Count);
            for (int index = 0; index < MaximumEntries; index++)
            {
                bool visible = index < count;
                if (entryRoots[index] != null) entryRoots[index].gameObject.SetActive(visible);
                if (entryButtons[index] != null) entryButtons[index].interactable = visible;
                if (!visible)
                {
                    ClearEntry(index);
                    continue;
                }

                CombatTimelineEntrySnapshot entry = timeline.Entries[index];
                Vector2 size = entry.IsCurrent ? new Vector2(66f, 66f) : new Vector2(52f, 46f);
                if (entryRoots[index] != null)
                {
                    entryRoots[index].sizeDelta = size;
                    Vector2 position = entryRoots[index].anchoredPosition;
                    position.y = top;
                    entryRoots[index].anchoredPosition = position;
                }
                top -= size.y + EntrySpacing;
                if (entryLabels[index] != null)
                {
                    entryLabels[index].fontSize = entry.IsCurrent ? 14 : 12;
                    entryLabels[index].text = FormatEntry(entry, index);
                }
                Image portrait = entryPortraits[index];
                if (portrait != null)
                {
                    portrait.rectTransform.sizeDelta = entry.IsCurrent ? new Vector2(40f, 40f) : new Vector2(24f, 24f);
                    bool player = entry.Faction == CombatUnitDisplayFaction.Player;
                    portrait.sprite = player ? playerPortrait : null;
                    portrait.preserveAspect = player;
                    portrait.color = player ? Color.white : new Color32(118, 69, 58, 255);
                    portrait.raycastTarget = false;
                    portrait.enabled = true;
                }
            }

            if (string.IsNullOrEmpty(focusedCombatantId) && count > 0)
                focusedCombatantId = timeline.Entries[0].Id;
            if (FindFocusedEntry() == null) focusedCombatantId = null;
            RefreshFocusText();
            if (keyboardFocusId != null)
            {
                GameObject nextFocus = null;
                for (int index = 0; index < count; index++)
                    if (timeline.Entries[index].Id == keyboardFocusId && entryButtons[index] != null)
                        nextFocus = entryButtons[index].gameObject;
                EventSystem.current.SetSelectedGameObject(nextFocus);
            }
        }

        public void Hide()
        {
            ClearContent();
            if (root != null) root.SetActive(false);
        }

        private void OnDisable() => ClearContent();

        private void AddFocusEvent(EventTrigger trigger, EventTriggerType eventType, int slot)
        {
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(_ =>
            {
                if (eventType == EventTriggerType.PointerEnter) hoveredSlot = slot;
                FocusEntry(slot);
            });
            trigger.triggers.Add(entry);
        }

        private void AddFocusExit(EventTrigger trigger, EventTriggerType eventType)
        {
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(_ => hoveredSlot = -1);
            trigger.triggers.Add(entry);
        }

        private void LateUpdate()
        {
            if (focusText == null) return;
            int count = snapshot == null ? 0 : Math.Min(MaximumEntries, snapshot.Entries.Count);
            int slot = hoveredSlot >= 0 && hoveredSlot < count ? hoveredSlot : -1;
            GameObject current = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (slot < 0)
                for (int index = 0; index < count; index++)
                    if (entryButtons[index] != null && entryButtons[index].gameObject == current) slot = index;
            if (slot >= 0) FocusEntry(slot);
            focusText.transform.parent.gameObject.SetActive(slot >= 0);
        }

        private void FocusEntry(int index)
        {
            if (snapshot == null || index >= Math.Min(MaximumEntries, snapshot.Entries.Count)) return;
            focusedCombatantId = snapshot.Entries[index].Id;

            RefreshFocusText();
        }

        private CombatTimelineEntrySnapshot FindFocusedEntry()
        {
            if (snapshot == null || string.IsNullOrEmpty(focusedCombatantId)) return null;
            int count = Math.Min(MaximumEntries, snapshot.Entries.Count);
            for (int index = 0; index < count; index++)
            {
                CombatTimelineEntrySnapshot entry = snapshot.Entries[index];
                if (string.Equals(entry.Id, focusedCombatantId, StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        private void RefreshFocusText()
        {
            if (focusText == null) return;
            CombatTimelineEntrySnapshot entry = FindFocusedEntry();
            if (entry == null)
            {
                focusText.text = string.Empty;
                return;
            }
            string faction = entry.Faction == CombatUnitDisplayFaction.Player ? "我方" : "敌方";
            string state = entry.IsCurrent ? "当前行动" : entry.QueuePosition >= 0
                ? "已排队 · 队列 " + (entry.QueuePosition + 1) : "蓄力（未排队）";
            focusText.text = entry.DisplayName + " · " + faction + "\nCT " +
                entry.CurrentCt.ToString("0.###", CultureInfo.InvariantCulture) + " / 门槛 " +
                entry.NextActionThreshold.ToString("0.###", CultureInfo.InvariantCulture) + "\n速度 " +
                entry.Speed + " · 调度 tick " + snapshot.CurrentTick + "\n" + state;
        }

        private string FormatEntry(CombatTimelineEntrySnapshot entry, int index)
        {
            bool player = entry.Faction == CombatUnitDisplayFaction.Player;
            return (entry.Id == inspectedTargetId ? "◆" : string.Empty) +
                (index + 1) + (entry.IsCurrent ? " 当前" : player ? " 我方" : entry.DisplayName);
        }

        private void ClearContent()
        {
            PresentPrediction(null);
            snapshot = null;
            focusedCombatantId = null;
            hoveredSlot = -1;
            inspectedTargetId = null;
            if (tickText != null) tickText.text = string.Empty;
            if (focusText != null)
            {
                focusText.text = string.Empty;
                focusText.transform.parent.gameObject.SetActive(false);
            }
            for (int index = 0; index < MaximumEntries; index++)
            {
                if (entryRoots[index] != null) entryRoots[index].gameObject.SetActive(false);
                if (entryButtons[index] != null) entryButtons[index].interactable = false;
                ClearEntry(index);
            }
        }

        private void ClearEntry(int index)
        {
            if (entryLabels[index] != null) entryLabels[index].text = string.Empty;
            if (entryPortraits[index] != null)
            {
                entryPortraits[index].sprite = null;
                entryPortraits[index].enabled = false;
            }
        }
    }
}
