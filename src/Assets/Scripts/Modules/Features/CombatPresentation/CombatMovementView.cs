using System;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects authoritative move options and keeps uncommitted selection local to the view.</summary>
    public sealed class CombatMovementView : MonoBehaviour
    {
        private static readonly Color ReachableOutlineColor = new Color32(0x23, 0x8C, 0xFF, 0xFF);

        [Serializable]
        private sealed class Cell
        {
            public int q;
            public int r;
            public Transform tile;
        }

        [SerializeField] private Camera battlefieldCamera;
        [SerializeField] private Cell[] cells = Array.Empty<Cell>();
        [SerializeField] private Material reachableMaterial;
        [SerializeField] private Material selectedMaterial;
        private readonly Dictionary<Vector2Int, LineRenderer> highlights = new Dictionary<Vector2Int, LineRenderer>();
        private readonly Dictionary<Vector2Int, Vector3> centers = new Dictionary<Vector2Int, Vector3>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private ICombatActionPreviewHandler previewHandler;
        private CombatActionBarView actionBar;
        private CombatHudSnapshot snapshot;
        private CombatMovementOption selected;
        private LineRenderer pathLine;
        public Action SelectionStarted { private get; set; }
        public bool IsSelecting { get; private set; }
        public CombatMovementOption Selected => selected;
        public IReadOnlyDictionary<Vector2Int, LineRenderer> Highlights => highlights;
        private bool CanInput => snapshot != null && snapshot.AcceptsCommands && !snapshot.IsPresenting;

        public void Configure(ICombatCommandHandler commands, CombatActionBarView bar)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            previewHandler = commands as ICombatActionPreviewHandler ??
                throw new ArgumentException("Movement requires the authoritative preview boundary.", nameof(commands));
            actionBar = bar != null ? bar : throw new ArgumentNullException(nameof(bar));
            if (battlefieldCamera == null || cells.Length == 0 || reachableMaterial == null || selectedMaterial == null)
                throw new InvalidOperationException("combat_movement_scene_references_missing");
            foreach (Cell cell in cells)
            {
                if (cell.tile == null) throw new InvalidOperationException("combat_movement_tile_missing");
                Vector3 center = cell.tile.position;
                float top = center.y;
                foreach (Renderer renderer in cell.tile.GetComponentsInChildren<Renderer>())
                    top = Mathf.Max(top, renderer.bounds.max.y);
                center.y = top + 0.01f;
                var key = new Vector2Int(cell.q, cell.r);
                centers.Add(key, center);
                LineRenderer ring = CreateLine("Reachable_" + cell.q + "_" + cell.r, reachableMaterial, 0.035f, ReachableOutlineColor);
                ring.loop = true;
                ring.positionCount = 6;
                for (int corner = 0; corner < 6; corner++)
                {
                    float radians = (30f + corner * 60f) * Mathf.Deg2Rad;
                    ring.SetPosition(corner, center + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * (0.92f / Mathf.Sqrt(3f)));
                }
                ring.gameObject.SetActive(false);
                highlights.Add(key, ring);
            }
            pathLine = CreateLine("MovementPathPreview", selectedMaterial, 0.065f, selectedMaterial.GetColor("_BaseColor"));
            pathLine.gameObject.SetActive(false);
        }

        public void Present(CombatHudSnapshot value)
        {
            // Every new authority projection invalidates any uncommitted candidate.
            snapshot = value;
            ClearSelection();
        }

        public void PresentAttackArea(CombatAttackAreaSnapshot area, CombatUnitPresentationHex? target = null, bool legal = false) =>
            PresentTargeting(null, 0, 0, area, target, legal);

        /// <summary>Shows the cast range and the effect cells for the currently pointed target.</summary>
        public void PresentCastingPreview(CombatUnitPresentationHex origin, int minimum, int maximum,
            CombatAttackAreaSnapshot area, CombatUnitPresentationHex? target, bool legal) =>
            PresentTargeting(origin, minimum, maximum, area, target, legal);

        private void PresentTargeting(CombatUnitPresentationHex? rangeOrigin, int minimum, int maximum,
            CombatAttackAreaSnapshot area, CombatUnitPresentationHex? target, bool legal)
        {
            ClearSelection();
            if (!CanInput) return;
            if (rangeOrigin.HasValue)
            {
                foreach (var pair in highlights)
                {
                    var position = new CombatUnitPresentationHex(pair.Key.x, pair.Key.y);
                    int distance = HexDistance(rangeOrigin.Value, position);
                    if (distance < minimum || distance > maximum) continue;
                    ShowRing(pair.Value, ReachableOutlineColor, 0.035f);
                }
            }
            IReadOnlyList<CombatAttackAreaCellSnapshot> shown = area?.Cells ??
                (target.HasValue ? new[] { new CombatAttackAreaCellSnapshot(target.Value, legal, string.Empty) } :
                    Array.Empty<CombatAttackAreaCellSnapshot>());
            foreach (CombatAttackAreaCellSnapshot cell in shown)
            {
                if (!highlights.TryGetValue(Key(cell.Position), out LineRenderer ring)) continue;
                Color color = cell.IsLegal ? selectedMaterial.GetColor("_BaseColor") : new Color(1f, 0.25f, 0.2f, 1f);
                ShowRing(ring, color, 0.06f);
            }
        }

        private static void ShowRing(LineRenderer ring, Color color, float width)
        {
            if (ring == null) return;
            var properties = new MaterialPropertyBlock();
            color.a = 1f;
            properties.SetColor("_BaseColor", color);
            ring.SetPropertyBlock(properties);
            ring.widthMultiplier = width;
            ring.gameObject.SetActive(true);
        }

        private static int HexDistance(CombatUnitPresentationHex left, CombatUnitPresentationHex right)
        {
            int q = left.Q - right.Q, r = left.R - right.R;
            return (Math.Abs(q) + Math.Abs(r) + Math.Abs(q + r)) / 2;
        }

        public void BeginSelection()
        {
            if (!CanInput || snapshot.Movement == null || snapshot.Movement.Options.Count == 0) return;
            SelectionStarted?.Invoke();
            IsSelecting = true;
            selected = null;
            foreach (CombatMovementOption option in snapshot.Movement.Options)
                if (highlights.TryGetValue(Key(option.Destination), out LineRenderer ring)) ring.gameObject.SetActive(true);
            RefreshControls();
        }

        public void Cancel()
        {
            if (!CanInput) return;
            ClearSelection();
        }

        public void Confirm()
        {
            if (!CanInput || !IsSelecting || selected == null) return;
            CombatMovementOption committed = selected;
            ClearSelection();
            previewHandler.ConfirmAction(committed.Candidate);
        }

        private void LateUpdate()
        {
            if (!CanInput || !IsSelecting) return;
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pointer = mouse.position.ReadValue();
            if (UiOwnsPointer(pointer)) return;
            if (mouse.rightButton.wasPressedThisFrame || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false))
            {
                Cancel();
                return;
            }
            CombatMovementOption hovered = null;
            if (TryPickCell(pointer, out CombatUnitPresentationHex cell))
                foreach (CombatMovementOption option in snapshot.Movement.Options)
                    if (option.Destination.Equals(cell)) { hovered = option; break; }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                selected = hovered;
                RefreshControls();
            }
            DrawPath(selected ?? hovered);
            actionBar?.PresentMovement(snapshot, IsSelecting, selected, selected ?? hovered);
        }

        public bool TryPickCell(Vector2 screenPoint, out CombatUnitPresentationHex picked)
        {
            picked = default;
            if (battlefieldCamera == null || cells.Length == 0 || UiOwnsPointer(screenPoint)) return false;
            Ray ray = battlefieldCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, cells[0].tile.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            Vector3 position = ray.GetPoint(distance);
            foreach (Cell cell in cells)
            {
                Vector3 delta = position - cell.tile.position;
                // Six perpendicular bisectors of the formal unit-spaced axial grid.
                if (Mathf.Abs(delta.x) <= 0.5f &&
                    Mathf.Abs(0.5f * delta.x + 0.8660254f * delta.z) <= 0.5f &&
                    Mathf.Abs(0.5f * delta.x - 0.8660254f * delta.z) <= 0.5f)
                {
                    picked = new CombatUnitPresentationHex(cell.q, cell.r);
                    return true;
                }
            }
            return false;
        }

        private bool UiOwnsPointer(Vector2 pointer)
        {
            EventSystem system = EventSystem.current;
            if (system == null) return false;
            uiHits.Clear();
            system.RaycastAll(new PointerEventData(system) { position = pointer }, uiHits);
            return uiHits.Count > 0;
        }

        private void DrawPath(CombatMovementOption option)
        {
            if (pathLine == null) return;
            pathLine.gameObject.SetActive(option != null);
            if (option == null) return;
            pathLine.positionCount = option.Path.Count + 1;
            pathLine.SetPosition(0, centers[Key(snapshot.Movement.Origin)]);
            for (int index = 0; index < option.Path.Count; index++)
                pathLine.SetPosition(index + 1, centers[Key(option.Path[index])]);
        }

        private void ClearSelection()
        {
            IsSelecting = false;
            selected = null;
            foreach (LineRenderer ring in highlights.Values)
                if (ring != null)
                {
                    ring.gameObject.SetActive(false);
                    ring.widthMultiplier = 0.035f;
                    var properties = new MaterialPropertyBlock();
                    properties.SetColor("_BaseColor", ReachableOutlineColor);
                    ring.SetPropertyBlock(properties);
                }
            if (pathLine != null) pathLine.gameObject.SetActive(false);
            RefreshControls();
        }

        private void RefreshControls() => actionBar?.PresentMovement(snapshot, IsSelecting, selected);
        private void OnDisable() => ClearSelection();
        private static Vector2Int Key(CombatUnitPresentationHex hex) => new Vector2Int(hex.Q, hex.R);
        private LineRenderer CreateLine(string name, Material material, float width, Color color)
        {
            var value = new GameObject(name);
            value.transform.SetParent(transform, false);
            LineRenderer line = value.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            // Outlines have their own colors without changing the shared filled-overlay palette.
            var properties = new MaterialPropertyBlock();
            color.a = 1f;
            properties.SetColor("_BaseColor", color);
            line.SetPropertyBlock(properties);
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }
}
