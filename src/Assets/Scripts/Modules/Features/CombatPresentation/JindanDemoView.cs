using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Linq;
using TianZhang.Gameplay.Contracts;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Displays the rehearsal state supplied by Adventure; owns no combat rules.</summary>
    public sealed class JindanDemoView : MonoBehaviour
    {
        [Serializable]
        public sealed class ActionButton
        {
            public string key;
            public Button button;
            [NonSerialized] public UnityAction listener;
        }

        [SerializeField] private GameObject preparationRoot;
        [SerializeField] private Text preparationDetails;
        [SerializeField] private GameObject combatRoot;
        [SerializeField] private GameObject combatBody;
        [SerializeField] private Text combatDetails;
        [SerializeField] private Text combatQuote;
        [SerializeField] private Text toggleLabel;
        [SerializeField] private GameObject flightActions;
        [SerializeField] private GameObject scaleActions;
        [SerializeField] private GameObject earthActions;
        [SerializeField] private GameObject tideActions;
        [SerializeField] private GameObject visibilityActions;
        [SerializeField] private GameObject fireActions;
        [SerializeField] private GameObject closeActions;
        [SerializeField] private Text quoteTitle;
        [SerializeField] private Text statusTemplate;
        [SerializeField] private CombatActionPreviewView ordinaryPreview;
        [SerializeField] private Text buildLabel;
        [SerializeField] private Text playerName;
        private readonly List<Text> statusLines = new List<Text>();
        [SerializeField] private GameObject statusRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private CombatActionBarView actionBar;
        private JindanHudSnapshot hud;
        private string selectedKey;
        public string SelectedKey => selectedKey;
        public string PersistentStatus => statusText == null ? string.Empty : statusText.text;
        public bool HasAbilities => hud?.Abilities.Count > 0;
        public float OptionsHeight
        {
            get
            {
                if (selectedKey == null) return 0;
                GameObject group = selectedKey switch { "fire_tab" => fireActions, "scale_tab" => scaleActions,
                    "flight_tab" => flightActions, "earth_tab" => earthActions, "tide_tab" => tideActions,
                    "visibility_tab" => visibilityActions, "close_tab" => closeActions, _ => null };
                int count = group == null ? 0 : group.GetComponentsInChildren<Button>(false).Count(b => b.gameObject.activeSelf);
                return Mathf.Ceil(count / 3f) * 38 + 8;
            }
        }
        [SerializeField] private ActionButton[] buttons = Array.Empty<ActionButton>();
        private bool bound;
        private bool collapsed;

        public event Action<string> ActionRequested;

        private void Awake() => BindButtons();

        private void BindButtons()
        {
            if (bound) return;
            foreach (ActionButton binding in buttons)
            {
                string key = binding.key;
                binding.listener = () => Request(key);
                binding.button.onClick.AddListener(binding.listener);
            }
            bound = true;
        }

        private void OnDestroy()
        {
            foreach (ActionButton binding in buttons)
                if (binding.button != null && binding.listener != null)
                    binding.button.onClick.RemoveListener(binding.listener);
        }

        private void Request(string key)
        {
            if (key is "flight_tab" or "scale_tab" or "earth_tab" or "tide_tab" or "visibility_tab" or "fire_tab" or "close_tab")
            {
                selectedKey = key;
                actionBar?.SelectJindan(GetButton(key));
                ApplySelection();
                ActionRequested?.Invoke(key); return;
            }
            if (key == "toggle_panel")
            {
                if (ordinaryPreview?.IsVisible != true) detailsRoot.SetActive(!detailsRoot.activeSelf);
                return;
            }
            if (key == "cancel_targeting") CloseCombatSelection();
            ActionRequested?.Invoke(key);
        }

        public void CloseCombatSelection()
        {
            selectedKey = null;
            actionBar?.SelectJindan(null);
            combatRoot.SetActive(false);
        }

        public void PresentHud(JindanHudSnapshot snapshot)
        {
            if (hud?.ActorId != snapshot?.ActorId) CloseCombatSelection();
            hud = snapshot;
            buildLabel.text = hud == null ? string.Empty : string.Join(" · ", hud.Abilities.Where(a => a.Key != "flight_tab").Select(a =>
                a.EffectId switch { "SPACE_SCALE" => "定尺", "YANG_RADIANCE" => "阳照", "YIN_CONCEAL" => "藏真", _ => a.Name }));
            buildLabel.rectTransform.anchoredPosition = new Vector2(playerName.preferredWidth + 12, -6);
            foreach (string key in new[] { "flight_tab", "scale_tab", "earth_tab", "tide_tab", "visibility_tab", "fire_tab" })
            {
                Button button = GetButton(key);
                if (button == null) continue;
                var ability = hud?.Abilities.FirstOrDefault(value => value.Key == key);
                button.gameObject.SetActive(ability != null);
                if (ability == null) continue;
                string shortName = ability.EffectId switch { "SPACE_SCALE" => "定尺", "YANG_RADIANCE" => "阳照", "YIN_CONCEAL" => "藏真", _ => ability.Name };
                button.GetComponentInChildren<Text>().text = CombatActionBarView.Caption(shortName, ability.Status);
                CombatActionBarView.PresentCooldown(button, ability.Cooldown);
            }
            GetButton("close_tab").gameObject.SetActive(HasAbilities);
            if (selectedKey != null && selectedKey != "close_tab" && !(hud?.Abilities.Any(value => value.Key == selectedKey) ?? false)) CloseCombatSelection();
            statusText.text = hud?.PersistentStatus ?? string.Empty;
            PresentStatusLines(statusText.text);
            statusRoot.SetActive(!string.IsNullOrEmpty(statusText.text));
            GetButton("toggle_panel").gameObject.SetActive(true);
            if (hud == null || ordinaryPreview?.IsVisible == true) detailsRoot.SetActive(false);
            ApplySelection();
        }

        private void ApplySelection()
        {
            flightActions.SetActive(selectedKey == "flight_tab");
            scaleActions.SetActive(selectedKey == "scale_tab");
            earthActions.SetActive(selectedKey == "earth_tab");
            tideActions.SetActive(selectedKey == "tide_tab");
            visibilityActions.SetActive(selectedKey == "visibility_tab");
            fireActions.SetActive(selectedKey == "fire_tab");
            closeActions.SetActive(selectedKey == "close_tab");
            foreach (var pair in new[] { ("fire_close", "fire_tab"), ("close_effect", "scale_tab"),
                ("visibility_close", "visibility_tab"), ("tide_close", "tide_tab"), ("earth_close", "earth_tab") })
                GetButton(pair.Item1).gameObject.SetActive(hud?.Abilities.Any(a => a.Key == pair.Item2) == true);
            var ability = hud?.Abilities.FirstOrDefault(value => value.Key == selectedKey);
            bool manifest = ability?.EffectId == "EARTH_MANIFEST";
            foreach (string key in new[] { "earth_wall", "earth_ramp" }) GetButton(key).gameObject.SetActive(manifest);
            foreach (string key in new[] { "earth_heavy", "earth_light", "earth_anchor", "earth_next_target" })
                GetButton(key).gameObject.SetActive(!manifest);
            GetButton("earth_swap").gameObject.SetActive(ability?.CanSwap == true);
            GetButton("swap_effect").gameObject.SetActive(false);
            GetButton("break_object").gameObject.SetActive(false); // Equipped demolition is already in the ordinary art slots.
        }

        private void PresentStatusLines(string value)
        {
            if (statusLines.Count == 0) statusLines.Add(statusTemplate);
            string[] lines = value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            while (statusLines.Count < lines.Length)
                statusLines.Add(Instantiate(statusTemplate.transform.parent.gameObject, statusRoot.transform).GetComponentInChildren<Text>());
            float top = 0;
            for (int i = 0; i < lines.Length; i++)
                statusLines[i].text = "<color=#edca82>" + (i == 0 ? "◈" : i == 1 ? "◇" : "◷") + "</color>  " + lines[i];
            float width = lines.Length == 0 ? 0 : Mathf.Min(410, statusLines.Take(lines.Length).Max(t => t.preferredWidth) + 20);
            for (int i = 0; i < statusLines.Count; i++)
            {
                Text label = statusLines[i]; label.transform.parent.gameObject.SetActive(i < lines.Length);
                if (i >= lines.Length) continue;
                var rect = (RectTransform)label.transform.parent;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
                rect.anchoredPosition = new Vector2(0, -top);
                rect.sizeDelta = new Vector2(width, 34);
                float height = Mathf.Max(33, label.preferredHeight + 8);
                rect.sizeDelta = new Vector2(width, height); top += height + 6;
            }
        }

        public Button GetButton(string key)
        {
            BindButtons();
            foreach (ActionButton binding in buttons)
                if (binding.key == key) return binding.button;
            return null;
        }

        public void PresentPreparation(string details)
        {
            BindButtons();
            preparationDetails.text = details ?? string.Empty;
            preparationRoot.SetActive(true);
            preparationRoot.transform.SetAsLastSibling();
            combatRoot.SetActive(false);
        }

        public void HidePreparation() => preparationRoot.SetActive(false);

        public void PresentCombat(string details, string quote, bool canInput, bool canConfirm, string title = "能力", string compact = "")
        {
            BindButtons();
            HidePreparation();
            combatDetails.text = (details ?? string.Empty) + "\n" + quote;
            combatQuote.text = compact;
            quoteTitle.text = title;
            combatRoot.SetActive(selectedKey != null);
            foreach (ActionButton binding in buttons)
            {
                if (!binding.button.transform.IsChildOf(combatBody.transform)) continue;
                binding.button.interactable = binding.key == "confirm" ? canInput && canConfirm : canInput;
            }
            foreach (var ability in hud?.Abilities ?? Array.Empty<JindanHudAbility>())
                GetButton(ability.Key).interactable = canInput && ability.Enabled;
            bool closable = hud?.Abilities.Any(a => a.HasActiveInstance) == true;
            GetButton("close_tab").interactable = canInput && closable;
            GetButton("close_tab").GetComponentInChildren<Text>().text = CombatActionBarView.Caption("收势", closable ? "选择实例" : "无主实例");
            foreach (var pair in new[] { ("fire_close", "fire_tab"), ("close_effect", "scale_tab"),
                ("visibility_close", "visibility_tab"), ("tide_close", "tide_tab"), ("earth_close", "earth_tab") })
                GetButton(pair.Item1).interactable = canInput && hud?.Abilities.Any(a => a.Key == pair.Item2 && a.HasActiveInstance) == true;
            combatBody.SetActive(true);
        }

        private void ApplyCollapse()
        {
            combatBody.SetActive(!collapsed);
            toggleLabel.text = collapsed ? "展开" : "收起";
        }

        public void HideAll()
        {
            preparationRoot.SetActive(false);
            combatRoot.SetActive(false);
            collapsed = false;
            PresentHud(null);
            CloseCombatSelection();
        }
    }
}
