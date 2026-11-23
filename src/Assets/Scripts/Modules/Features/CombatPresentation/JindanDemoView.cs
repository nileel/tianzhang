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
        [SerializeField] private Text toggleLabel;
        [SerializeField] private GameObject flightActions;
        [SerializeField] private GameObject scaleActions;
        [SerializeField] private GameObject sealActions;
        [SerializeField] private GameObject earthActions;
        [SerializeField] private GameObject tideActions;
        [SerializeField] private GameObject visibilityActions;
        [SerializeField] private GameObject fireActions;
        [SerializeField] private GameObject closeActions;
        [SerializeField] private Text statusTemplate;
        [SerializeField] private CombatActionPreviewView ordinaryPreview;
        [SerializeField] private Text buildLabel;
        [SerializeField] private Text playerName;
        [SerializeField] private Text fireCenterLabel;
        private readonly List<Text> fieldLabels = new();
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
        public void PresentFireCenter(Vector3? viewport)
        {
            fireCenterLabel.gameObject.SetActive(viewport.HasValue);
            if (!viewport.HasValue) return;
            Vector3 point = viewport.Value;
            // The V3 HUD fills the camera viewport at every supported resolution.
            var anchor = new Vector2(Mathf.Clamp(point.x, .05f, .95f), Mathf.Clamp(point.y, .18f, .90f));
            var rect = fireCenterLabel.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            fireCenterLabel.text = point.x < .05f ? "← 阵心" : point.x > .95f ? "阵心 →" :
                point.y > .90f ? "↑ 阵心" : point.y < .18f ? "↓ 阵心" : "阵心";
        }

        public void PresentFieldLabels(Camera camera, CombatBattlefieldSurface surface, IReadOnlyList<CombatUnitPresentationHex> embers,
            CombatUnitPresentationHex? sun, CombatUnitPresentationHex? moon, IReadOnlyList<CombatEarthObjectSnapshot> earth,
            IReadOnlyList<CombatUnitPresentationHex> domainFire = null, CombatUnitPresentationHex? movementGoal = null)
        {
            int used = 0;
            void Mark(CombatUnitPresentationHex cell, string caption)
            {
                Vector3 point = camera.WorldToViewportPoint(surface.Center(cell));
                if (point.z <= 0 || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1) return;
                if (used == fieldLabels.Count)
                {
                    var copy = Instantiate(fireCenterLabel, fireCenterLabel.transform.parent);
                    copy.name = "PublicFieldLabel";
                    fieldLabels.Add(copy);
                }
                var label = fieldLabels[used++];
                label.text = caption;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = (Vector2)point;
                label.gameObject.SetActive(true);
            }
            if (embers != null) foreach (var cell in embers) Mark(cell, "余火");
            if (domainFire != null) foreach (var cell in domainFire) Mark(cell, "长明地火");
            if (movementGoal.HasValue) Mark(movementGoal.Value, "演练目标");
            if (sun.HasValue) Mark(sun.Value, "日坛");
            if (moon.HasValue) Mark(moon.Value, "月坛");
            if (earth != null)
                foreach (var obj in earth)
                {
                    if (!obj.IsRamp) continue;
                    bool aIsLower = surface.Center(obj.A).y < surface.Center(obj.B).y;
                    Mark(obj.A, aIsLower ? "坡脚" : "坡顶");
                    Mark(obj.B, aIsLower ? "坡顶" : "坡脚");
                }
            for (int i = used; i < fieldLabels.Count; i++) fieldLabels[i].gameObject.SetActive(false);
        }
        public float OptionsHeight
        {
            get
            {
                if (selectedKey == null) return 0;
                GameObject group = selectedKey switch { "fire_tab" => fireActions, "scale_tab" => scaleActions, "seal_tab" => sealActions,
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
            if (key is "flight_tab" or "scale_tab" or "seal_tab" or "earth_tab" or "tide_tab" or "visibility_tab" or "fire_tab" or "close_tab")
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
            ActionRequested?.Invoke(key);
        }

        public void CloseCombatSelection()
        {
            if (selectedKey != null) ordinaryPreview.Present(null);
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
            foreach (string key in new[] { "flight_tab", "scale_tab", "seal_tab", "earth_tab", "tide_tab", "visibility_tab", "fire_tab" })
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
            sealActions.SetActive(selectedKey == "seal_tab");
            earthActions.SetActive(selectedKey == "earth_tab");
            tideActions.SetActive(selectedKey == "tide_tab");
            visibilityActions.SetActive(selectedKey == "visibility_tab");
            fireActions.SetActive(selectedKey == "fire_tab");
            closeActions.SetActive(selectedKey == "close_tab");
            foreach (var pair in new[] { ("fire_close", "fire_tab"), ("close_effect", "scale_tab"), ("seal_close", "seal_tab"),
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
            if (key == "confirm") return ordinaryPreview.ConfirmButton;
            if (key == "cancel_targeting") return ordinaryPreview.CancelButton;
            foreach (ActionButton binding in buttons)
                if (binding.key == key) return binding.button;
            return null;
        }

        public void PresentPreparation(string details, bool trial, bool result, bool showDetails)
        {
            BindButtons();
            if (preparationDetails.text != details)
                preparationDetails.GetComponentInParent<ScrollRect>(true).verticalNormalizedPosition = 1;
            preparationDetails.text = details ?? string.Empty;
            foreach (string key in new[] { "stage1", "stage2", "stage3", "enter", "cancel_preparation" })
                GetButton(key).gameObject.SetActive(!result);
            foreach (string key in new[] { "basic", "prep_details", "prep_save" })
                GetButton(key).gameObject.SetActive(trial && !result);
            GetButton("earth_toggle").gameObject.SetActive(!trial && !result);
            GetButton("fire_side").gameObject.SetActive(!trial);
            GetButton("preset_next").gameObject.SetActive(!trial);
            GetButton("cancel_preparation").GetComponentInChildren<Text>(true).text = trial ? "返回关中城" : "返回冒险";
            GetButton("prep_details").GetComponentInChildren<Text>(true).text = showDetails ? "返回遭遇" : "查看角色";
            GetButton("enter").GetComponentInChildren<Text>(true).text = trial ? "进入此遭遇" : "进入演武";
            GetButton("retry").gameObject.SetActive(result);
            GetButton("choose_challenge").gameObject.SetActive(result);
            preparationRoot.SetActive(true);
            preparationRoot.transform.SetAsLastSibling();
            combatRoot.SetActive(false);
        }

        public void HidePreparation() => preparationRoot.SetActive(false);

        public void PresentCombat(string details, string quote, bool canInput, bool canConfirm, string title,
            string compact, string hint)
        {
            BindButtons();
            HidePreparation();
            combatDetails.text = (details ?? string.Empty) + "\n" + quote;
            combatRoot.SetActive(selectedKey != null);
            if (selectedKey != null)
                ordinaryPreview.PresentJindan(title, compact, hint, combatDetails.text, canInput && canConfirm);
            foreach (ActionButton binding in buttons)
            {
                if (!binding.button.transform.IsChildOf(combatBody.transform)) continue;
                binding.button.interactable = canInput;
            }
            foreach (var ability in hud?.Abilities ?? Array.Empty<JindanHudAbility>())
                GetButton(ability.Key).interactable = canInput && ability.Enabled;
            bool closable = hud?.Abilities.Any(a => a.HasActiveInstance) == true;
            GetButton("close_tab").interactable = canInput && closable;
            GetButton("close_tab").GetComponentInChildren<Text>().text = CombatActionBarView.Caption("收势", closable ? "选择实例" : "无主实例");
            foreach (var pair in new[] { ("fire_close", "fire_tab"), ("close_effect", "scale_tab"), ("seal_close", "seal_tab"),
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
