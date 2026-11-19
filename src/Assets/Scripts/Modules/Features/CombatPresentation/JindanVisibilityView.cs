using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Unscaled keyboard edges and a modal projection. Adventure owns binding to combat authority.</summary>
    public sealed class JindanVisibilityView : MonoBehaviour
    {
        [SerializeField] private GameObject dialogRoot;
        [SerializeField] private Text details;
        [SerializeField] private Text bindingsText;
        [SerializeField] private Button skipButton, autoButton, exitButton, holdBinding, firstBinding, secondBinding;
        [SerializeField] private GameObject yangMarker, yinMarker;
        [SerializeField] private GameObject yangAltarPrefab, yinAltarPrefab, yangRangePrefab, yinRangePrefab;
        private JindanVisibilityDomain yangRange, yinRange;
        private Key hold, first, second;
        private long eventId;
        private int rebinding = -1;
        private bool active, configured;
        public event Action<long, long, bool, bool, int> InputRequested;
        public event Action<long, bool> SkipRequested;
        public event Action ExitRequested;
        private void Awake()
        {
            skipButton.onClick.AddListener(() => SkipRequested?.Invoke(eventId, false));
            autoButton.onClick.AddListener(() => SkipRequested?.Invoke(eventId, true));
            exitButton.onClick.AddListener(() => ExitRequested?.Invoke());
            holdBinding.onClick.AddListener(() => Rebind(0));
            firstBinding.onClick.AddListener(() => Rebind(1));
            secondBinding.onClick.AddListener(() => Rebind(2));
        }
        public void ConfigureKeys(string holdName, string firstName, string secondName)
        {
            if (configured) return;
            hold = Parse(holdName); first = Parse(firstName); second = Parse(secondName); configured = true;
        }
        private static Key Parse(string name) => Enum.TryParse(name, true, out Key key) && Enum.IsDefined(typeof(Key), key) && key != Key.None
            ? key : throw new ArgumentException("visibility_input_key_invalid:" + name);
        private void Rebind(int index) { rebinding = index; EventSystem.current?.SetSelectedGameObject(null); }
        public void Present(bool paused, long id, string message, bool started, int pulseCount)
        {
            if (paused && (!active || eventId != id)) EventSystem.current?.SetSelectedGameObject(null);
            active = paused; eventId = id;
            dialogRoot.SetActive(paused);
            if (!paused) { rebinding = -1; return; }
            dialogRoot.transform.SetAsLastSibling(); details.text = message.Replace("[破绽键]", (pulseCount % 2 == 1 ? first : second).ToString());
            holdBinding.interactable = firstBinding.interactable = secondBinding.interactable = !started;
            bindingsText.text = rebinding >= 0 ? "按下新的键；Esc取消；三键不得重复" : "维持 " + hold + "　破绽 " + first + " / " + second;
        }
        public void PresentSupports(bool yangActive, bool yangDestroyed, bool yinActive, bool yinDestroyed)
        {
            if (yangRange == null)
            {
                if (!yangMarker.activeInHierarchy) return;
                var board = yangMarker.GetComponentInParent<CombatBattlefieldSurface>();
                yangMarker = ReplaceAltar(yangMarker, yangAltarPrefab);
                yinMarker = ReplaceAltar(yinMarker, yinAltarPrefab);
                yangRange = MakeRange(yangRangePrefab, board.transform);
                yinRange = MakeRange(yinRangePrefab, board.transform);
            }
            Support(yangMarker, yangActive, yangDestroyed);
            Support(yinMarker, yinActive, yinDestroyed);
            PresentRange(yangRange, yangActive);
            PresentRange(yinRange, yinActive);
        }

        private static GameObject ReplaceAltar(GameObject marker, GameObject prefab)
        {
            marker.transform.Find("Intact").gameObject.SetActive(false);
            marker.transform.Find("Failed").gameObject.SetActive(false);
            return Instantiate(prefab, marker.transform, false);
        }

        private static JindanVisibilityDomain MakeRange(GameObject prefab, Transform board)
        {
            // WholeBoardRange was baked in the actual fixed board's world coordinates.
            var root = Instantiate(prefab, Vector3.zero, Quaternion.identity, board);
            root.name = prefab.name;
            return new JindanVisibilityDomain(root);
        }

        private static void PresentRange(JindanVisibilityDomain range, bool active)
        {
            range.Set(active);
            if (active && range.Opacity < 1f || !active && range.Root.activeSelf)
                range.Tick(Time.deltaTime);
        }

        private static void Support(GameObject marker, bool deployed, bool destroyed)
        {
            marker.transform.Find("Intact").gameObject.SetActive(!destroyed);
            marker.transform.Find("Failed").gameObject.SetActive(destroyed);
            marker.transform.Find("Intact/Glow").gameObject.SetActive(deployed && !destroyed);
        }
        private void Update()
        {
            if (!active || Keyboard.current == null) return;
            var keyboard = Keyboard.current;
            if (rebinding >= 0)
            {
                var pressed = keyboard.allKeys.Where(key => key.wasPressedThisFrame).ToArray();
                if (pressed.Length != 1) return;
                Key next = pressed[0].keyCode;
                if (next == Key.Escape) { rebinding = -1; return; }
                if (next == hold || next == first || next == second) return;
                if (rebinding == 0) hold = next; else if (rebinding == 1) first = next; else second = next;
                rebinding = -1; return;
            }
            int mask = 0;
            if (keyboard[first].wasPressedThisFrame) mask |= 1;
            if (keyboard[second].wasPressedThisFrame) mask |= 2;
            // Holding another response key makes a chord invalid, even if its edge arrived earlier.
            if (mask != 0 && keyboard[first].isPressed && keyboard[second].isPressed) mask = 3;
            InputRequested?.Invoke(eventId, (long)(Time.realtimeSinceStartupAsDouble * 1000),
                keyboard[hold].wasPressedThisFrame, keyboard[hold].wasReleasedThisFrame, mask);
        }
    }
}
