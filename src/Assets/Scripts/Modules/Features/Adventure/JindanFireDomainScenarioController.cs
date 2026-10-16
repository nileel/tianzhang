using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.Adventure
{
    /// <summary>Serialized fixture input and projection only; all decisions belong to State.</summary>
    public sealed class JindanFireDomainScenarioController : MonoBehaviour
    {
        [SerializeField] private FireDomainConfig configuration = new FireDomainConfig();
        [SerializeField] private Image[] cellMarkers = Array.Empty<Image>();
        [SerializeField] private Text[] cellLabels = Array.Empty<Text>();
        [SerializeField] private RectTransform[] unitMarkers = Array.Empty<RectTransform>();
        [SerializeField] private Text[] unitLabels = Array.Empty<Text>();
        [SerializeField] private RectTransform domainCenter;
        [SerializeField] private Text centerLabel, stateText, previewText;
        private JindanFireDomainScenarioState state;
        private FireDomainPlan preview;
        private Vector2Int selected;
        private string selectedUnit = "Friend";
        public FireDomainConfig Configuration => JindanFireDomainScenarioState.Clone(configuration);
        public JindanFireDomainScenarioState State => state;
        public string PanelText => stateText.text;
        public int CellMarkerCount => cellMarkers.Length;
        public int UnitMarkerCount => unitMarkers.Length;
        public static Vector2 BoardPosition(Vector2Int p) => new Vector2((p.x + .5f * p.y) * 155, p.y * 135);

        private void Awake() => ResetFixture();
        private void OnDisable() { state?.EndBattle(); if (state != null) Refresh(); }
        private void OnDestroy() => state?.EndBattle();

        public void ConfigurePresentation(Image[] cells, Text[] labels, RectTransform[] units, Text[] names,
            RectTransform center, Text centerText, Text status, Text plan)
        {
            cellMarkers = cells; cellLabels = labels; unitMarkers = units; unitLabels = names;
            domainCenter = center; centerLabel = centerText; stateText = status; previewText = plan;
        }
        public void ResetFixture(FireDomainConfig variant = null)
        {
            state?.EndBattle();
            state = new JindanFireDomainScenarioState(variant ?? configuration);
            preview = null; selected = Vector2Int.zero; selectedUnit = "Friend"; Refresh();
        }
        public FireDomainSnapshot Capture() => state.Capture();
        public FireDomainPlan Preview(string operation)
        {
            preview = state.Preview(operation); Refresh(); return preview;
        }
        public bool Commit(FireDomainPlan plan)
        {
            bool success = state.Commit(plan);
            if (!success && state.LastFailure == "stale_preview") preview = state.Preview(plan.Operation);
            Refresh(); return success;
        }
        public bool Apply(string operation) => Commit(Preview(operation));
        public bool Counter(Vector2Int p, string kind) { bool ok = state.Counter(p, kind); Refresh(); return ok; }
        public bool Move(string unit, Vector2Int p) { bool ok = state.Move(unit, p); Refresh(); return ok; }
        public bool CloseMain(string reason) { bool ok = state.CloseMain(reason); Refresh(); return ok; }
        public bool AttackCenter(int damage) { bool ok = state.AttackCenter(damage); Refresh(); return ok; }
        public void EndBattle() { state.EndBattle(); Refresh(); }
        public bool TryOccupy(string seat) { bool ok = state.TryOccupy(seat); Refresh(); return ok; }

        // Persisted Button UnityEvents use this same input path as direct tests.
        public void Command(string command)
        {
            if (command.StartsWith("cell:"))
            {
                var parts = command.Split(':'); selected = new Vector2Int(int.Parse(parts[1]), int.Parse(parts[2]));
            }
            else if (command.StartsWith("unit:")) selectedUnit = command.Substring(5);
            else if (command.StartsWith("preview:")) Preview(command.Substring(8));
            else if (command == "commit") { if (preview != null) Commit(preview); }
            else if (command == "manifest" || command == "open" || command == "cycle") Apply(command);
            else if (command == "move") Move(selectedUnit, selected);
            else if (command == "attack") AttackCenter(10);
            else if (command == "close" || command == "source_exit" || command == "domain_exit") CloseMain(command);
            else if (command == "end") EndBattle();
            else if (command == "reset") ResetFixture();
            else Counter(selected, command);
            Refresh();
        }

        public void Refresh()
        {
            if (state == null || stateText == null) return;
            var s = Capture();
            var cfg = configuration;
            for (int i = 0; i < cellMarkers.Length; i++)
            {
                var c = s.cells[i];
                cellMarkers[i].color = c.normal > 0 ? new Color(.9f, .24f, .07f) : c.ember > 0
                    ? new Color(.65f, .39f, .12f) : c.water ? new Color(.08f, .4f, .65f) : c.isolated
                    ? new Color(.3f, .2f, .4f) : c.fuel == 0 ? new Color(.13f, .15f, .17f) : new Color(.19f, .29f, .27f);
                string fire = c.normal > 0 ? "FIRE " + c.normal : c.ember > 0 ? "EMBER " + c.ember
                    : c.water ? "WATER" : c.isolated ? "ISOLATED" : c.fuel == 0 ? "NO FUEL" : "CLEAR";
                string initial = cfg.initial.Contains(c.coordinate) ? " *" : "";
                cellLabels[i].text = c.coordinate.x + "," + c.coordinate.y + initial + "\n" + fire + "\nfuel " + c.fuel;
            }
            for (int i = 0; i < unitMarkers.Length; i++)
            {
                unitMarkers[i].anchoredPosition = BoardPosition(s.units[i].coordinate) + new Vector2(0, 47);
                unitLabels[i].text = s.units[i].id + " HP " + s.units[i].hp;
            }
            domainCenter.anchoredPosition = BoardPosition(cfg.center) + new Vector2(0, -47);
            centerLabel.text = "CENTER " + s.centerHp;
            var cell = s.cells.First(c => c.coordinate == selected);
            stateText.text = "FIRE DOMAIN / isolated functional fixture\nCycle " + s.cycle + "   Action " + s.actions + "   Spirit " + s.spirit
                + "\nSurface " + s.SurfaceUsed + "/" + s.surfaceCapacity + "   Environment " + s.EnvironmentUsed + "/" + s.environmentCapacity
                + "\nReached " + s.Reached + "/" + cfg.maxReached + "   Spread budget " + s.propagation
                + "\nSource: " + s.sourceRef + "\nCombo: " + s.comboInstanceId
                + "\nSource seat: " + s.sourceMapping + "\nDomain seat: " + s.domainMapping
                + "\nPaid manifest/support/open: " + s.manifestPaid + "/" + s.supportPaid + "/" + s.openPaid
                + "\nPaid maintenance/expansion/tail: " + s.maintenancePaid + "/" + s.expansionPaid + "/" + s.tailPaid
                + "\nFuel paid " + s.fuelPaid + "   Closed " + s.mainClosed + "   Ended " + s.ended
                + "\nSelected " + selected + "   " + selectedUnit + "\nFuel " + cell.fuel + "   Normal " + cell.normal + "   Ember " + cell.ember
                + "\n" + string.Join("   ", s.units.Select(u => u.id + "=" + u.hp)) + "\nFailure: " + state.LastFailure;
            previewText.text = preview == null ? "Preview costs before commit. * initial footprint; board edge = radius 2.\nCountermeasures are test inputs. Units have no soul channel."
                : "PREVIEW " + preview.Operation + " / " + preview.Failure + "\nAction " + preview.Actions + " Spirit " + preview.Spirit
                + " (maintenance " + preview.Maintenance + ", expansion " + preview.Expansion + ", tail " + preview.Tail + ")\nNodes "
                + string.Join(" ", preview.Nodes) + " / revision " + preview.Revision;
        }
    }
}
