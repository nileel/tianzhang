using System;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>One selectable unit or cell offered by the authoritative skill quote.</summary>
    public readonly struct CombatTargetingHint
    {
        public CombatTargetingHint(CombatUnitPresentationHex cell, bool legal)
        {
            Cell = cell;
            IsLegal = legal;
        }

        public CombatUnitPresentationHex Cell { get; }
        public bool IsLegal { get; }
    }

    /// <summary>Projects authoritative move options and keeps uncommitted selection local to the view.</summary>
    public sealed class CombatMovementView : MonoBehaviour
    {
        // Cool reachable range, warm destination/target, amber effect cells and red rejection.
        private static readonly Color ReachableOutlineColor = new Color32(0x23, 0x8C, 0xFF, 0xFF);
        private static readonly Color DestinationColor = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        private static readonly Color LegalTargetColor = new Color32(0xE8, 0xC4, 0x6A, 0xFF);
        private static readonly Color CurrentTargetColor = new Color32(0xFF, 0xE7, 0xA0, 0xFF);
        private static readonly Color EffectCellColor = new Color32(0xFF, 0x9F, 0x3C, 0xFF);
        private static readonly Color IllegalColor = new Color32(0xFF, 0x40, 0x33, 0xFF);
        // The single ground anchor for the inspected unit replaces the old screen-space box.
        private static readonly Color InspectionColor = new Color32(0xFF, 0xC8, 0x5C, 0xFF);
        private const float ReachableWidth = 0.035f;
        private const float DestinationWidth = 0.07f;
        private const float LegalTargetWidth = 0.045f;
        private const float CurrentTargetWidth = 0.09f;
        private const float EffectWidth = 0.06f;
        private const float IllegalWidth = 0.06f;
        private const float InspectionWidth = 0.08f;
        private const float PathNodeRadius = 0.12f;
        private const float SelectionBorderOffset = 0.01f;

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
        private readonly List<LineRenderer> pathNodes = new List<LineRenderer>();
        private ICombatActionPreviewHandler previewHandler;
        private CombatActionBarView actionBar;
        private CombatActionPreviewView previewView;
        private CombatHudSnapshot snapshot;
        private CombatMovementOption selected;
        private LineRenderer pathLine;
        private LineRenderer pathArrow;
        private LineRenderer inspectionRing;
        private CombatUnitPresentationHex? inspectionCell;
        private Vector2Int? highlightedDestination;
        private bool ownsPreview;
        public Action SelectionStarted { private get; set; }
        public bool IsSelecting { get; private set; }
        public CombatMovementOption Selected => selected;
        public IReadOnlyDictionary<Vector2Int, LineRenderer> Highlights => highlights;
        public LineRenderer InspectionRing => inspectionRing;
        public CombatUnitPresentationHex? InspectionCell => inspectionCell;
        public IReadOnlyList<LineRenderer> PathNodeLines => pathNodes;
        public LineRenderer PathDirectionArrow => pathArrow;
        private bool CanInput => snapshot != null && snapshot.AcceptsCommands && !snapshot.IsPresenting;

        public void Configure(ICombatCommandHandler commands, CombatActionBarView bar,
            CombatActionPreviewView movementPopup = null)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            previewHandler = commands as ICombatActionPreviewHandler ??
                throw new ArgumentException("Movement requires the authoritative preview boundary.", nameof(commands));
            actionBar = bar != null ? bar : throw new ArgumentNullException(nameof(bar));
            previewView = movementPopup;
            if (battlefieldCamera == null || cells.Length == 0 || reachableMaterial == null || selectedMaterial == null)
                throw new InvalidOperationException("combat_movement_scene_references_missing");
            foreach (Cell cell in cells)
            {
                if (cell.tile == null) throw new InvalidOperationException("combat_movement_tile_missing");
                Vector3 center = cell.tile.position;
                center.y = TileBodyTop(cell.tile, center.y) + SelectionBorderOffset;
                var key = new Vector2Int(cell.q, cell.r);
                centers.Add(key, center);
                LineRenderer ring = CreateLine("Reachable_" + cell.q + "_" + cell.r, reachableMaterial, ReachableWidth, ReachableOutlineColor);
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
            pathLine = CreateLine("MovementPathPreview", selectedMaterial, 0.065f, DestinationColor);
            pathLine.gameObject.SetActive(false);
            pathArrow = CreateLine("MovementPathDirection", selectedMaterial, 0.07f, DestinationColor);
            pathArrow.positionCount = 3;
            pathArrow.gameObject.SetActive(false);
            inspectionRing = CreateLine("InspectionCellRing", selectedMaterial, InspectionWidth, InspectionColor);
            inspectionRing.loop = true;
            inspectionRing.positionCount = 6;
            inspectionRing.gameObject.SetActive(false);
        }

        /// <summary>Single tile-body ring marking the inspected unit's cell; independent of movement selection.</summary>
        public void SetInspectionHighlight(CombatUnitPresentationHex? cell)
        {
            if (inspectionRing == null) return;
            if (!cell.HasValue || !centers.TryGetValue(Key(cell.Value), out Vector3 center))
            {
                inspectionCell = null;
                inspectionRing.gameObject.SetActive(false);
                return;
            }
            if (inspectionCell.HasValue && inspectionCell.Value.Equals(cell.Value) &&
                inspectionRing.gameObject.activeSelf) return;
            inspectionCell = cell;
            for (int corner = 0; corner < 6; corner++)
            {
                float radians = (30f + corner * 60f) * Mathf.Deg2Rad;
                inspectionRing.SetPosition(corner,
                    center + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * (0.92f / Mathf.Sqrt(3f)));
            }
            ShowRing(inspectionRing, InspectionColor, InspectionWidth);
        }

        public void Present(CombatHudSnapshot value)
        {
            // Every new authority projection invalidates any uncommitted candidate.
            snapshot = value;
            ClearSelection();
        }

        public void PresentAttackArea(CombatAttackAreaSnapshot area, CombatUnitPresentationHex? target = null, bool legal = false) =>
            PresentTargeting(null, 0, 0, area, target, legal, null);

        /// <summary>Shows the cast range, the legal unit targets, and the effect cells of the pointed target.</summary>
        public void PresentCastingPreview(CombatUnitPresentationHex origin, int minimum, int maximum,
            CombatAttackAreaSnapshot area, CombatUnitPresentationHex? target, bool legal,
            IReadOnlyList<CombatTargetingHint> targets = null) =>
            PresentTargeting(origin, minimum, maximum, area, target, legal, targets);

        private void PresentTargeting(CombatUnitPresentationHex? rangeOrigin, int minimum, int maximum,
            CombatAttackAreaSnapshot area, CombatUnitPresentationHex? target, bool legal,
            IReadOnlyList<CombatTargetingHint> targets)
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
                    ShowRing(pair.Value, ReachableOutlineColor, ReachableWidth);
                }
            }
            // Legal targets stay marked before the pointer reaches them, so the range and the
            // selectable units remain distinguishable from the effect cells of one candidate.
            if (targets != null)
                foreach (CombatTargetingHint hint in targets)
                {
                    if (!hint.IsLegal || !highlights.TryGetValue(Key(hint.Cell), out LineRenderer ring)) continue;
                    ShowRing(ring, LegalTargetColor, LegalTargetWidth);
                }
            IReadOnlyList<CombatAttackAreaCellSnapshot> shown = area?.Cells ??
                (target.HasValue ? new[] { new CombatAttackAreaCellSnapshot(target.Value, legal, string.Empty) } :
                    Array.Empty<CombatAttackAreaCellSnapshot>());
            foreach (CombatAttackAreaCellSnapshot cell in shown)
            {
                if (!highlights.TryGetValue(Key(cell.Position), out LineRenderer ring)) continue;
                ShowRing(ring, cell.IsLegal ? EffectCellColor : IllegalColor, cell.IsLegal ? EffectWidth : IllegalWidth);
            }
            // The pointed cell is the current target and always outranks the range or area color.
            if (target.HasValue && highlights.TryGetValue(Key(target.Value), out LineRenderer current))
                ShowRing(current, legal ? CurrentTargetColor : IllegalColor,
                    legal ? CurrentTargetWidth : IllegalWidth);
        }

        /// <summary>
        /// Returns the top of the cell's terrain body. Units and obstacles share the same cell
        /// root, so only the tile's own measured alignment subtree may raise the selection ring;
        /// a rock sitting in the cell must never lift it off the ground.
        /// </summary>
        private static float TileBodyTop(Transform tile, float fallback)
        {
            Transform body = tile.Find("MeasuredFbxAlignment");
            Renderer[] renderers = body != null
                ? body.GetComponentsInChildren<Renderer>()
                : tile.GetComponents<Renderer>();
            float top = fallback;
            foreach (Renderer renderer in renderers)
                top = Mathf.Max(top, renderer.bounds.max.y);
            return top;
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
            highlightedDestination = null;
            foreach (CombatMovementOption option in snapshot.Movement.Options)
                if (highlights.TryGetValue(Key(option.Destination), out LineRenderer ring)) ring.gameObject.SetActive(true);
            ownsPreview = previewView != null;
            PresentMovementPreview(false);
            RefreshControls();
        }

        public void Cancel()
        {
            if (!CanInput) return;
            ClearSelection();
        }

        public void Confirm()
        {
            if (!CanInput || !IsSelecting || selected == null || selected.Candidate == null) return;
            CombatMovementOption committed = selected;
            // Close the popup and range before the authority projection can repaint either.
            ClearSelection();
            previewHandler.ConfirmAction(committed.Candidate);
        }

        private void LateUpdate()
        {
            if (!CanInput || !IsSelecting || snapshot.Movement == null) return;
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pointer = mouse.position.ReadValue();
            // Cancellation must work even while the pointer is over the popup or another UI owner.
            if (mouse.rightButton.wasPressedThisFrame || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false))
            {
                Cancel();
                return;
            }
            bool uiOwnsPointer = UiOwnsPointer(pointer);
            bool pointerOnBoard = false;
            CombatMovementOption hovered = null;
            if (!uiOwnsPointer && TryPickCell(pointer, out CombatUnitPresentationHex cell))
            {
                pointerOnBoard = true;
                foreach (CombatMovementOption option in snapshot.Movement.Options)
                    if (option.Destination.Equals(cell)) { hovered = option; break; }
            }
            // Hover is only a preview; it never commits and never clears an ongoing selection.
            selected = hovered;
            HighlightDestination(hovered);
            DrawPath(hovered);
            PresentMovementPreview(pointerOnBoard);
            actionBar?.PresentMovement(snapshot, IsSelecting, hovered);
            if (mouse.leftButton.wasPressedThisFrame && hovered != null) Confirm();
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

        /// <summary>Keeps exactly one gold destination; the previous hover returns to cyan.</summary>
        private void HighlightDestination(CombatMovementOption hovered)
        {
            if (highlightedDestination.HasValue &&
                highlights.TryGetValue(highlightedDestination.Value, out LineRenderer previous) &&
                (hovered == null || Key(hovered.Destination) != highlightedDestination.Value))
                ShowRing(previous, ReachableOutlineColor, ReachableWidth);
            highlightedDestination = null;
            if (hovered == null || !highlights.TryGetValue(Key(hovered.Destination), out LineRenderer ring)) return;
            ShowRing(ring, DestinationColor, DestinationWidth);
            highlightedDestination = Key(hovered.Destination);
        }

        private void DrawPath(CombatMovementOption option)
        {
            if (pathLine == null) return;
            bool visible = option != null && snapshot?.Movement != null;
            pathLine.gameObject.SetActive(visible);
            if (pathArrow != null) pathArrow.gameObject.SetActive(visible);
            if (!visible)
            {
                HidePathNodes(0);
                return;
            }
            pathLine.positionCount = option.Path.Count + 1;
            Vector3 origin = centers[Key(snapshot.Movement.Origin)];
            pathLine.SetPosition(0, origin);
            for (int index = 0; index < option.Path.Count; index++)
                pathLine.SetPosition(index + 1, centers[Key(option.Path[index])]);
            // Every intermediate cell carries a warm node; the destination gets the arrowhead.
            int nodeCount = Math.Max(0, option.Path.Count - 1);
            EnsurePathNodes(nodeCount);
            for (int index = 0; index < nodeCount; index++)
            {
                Vector3 nodeCenter = centers[Key(option.Path[index])];
                LineRenderer node = pathNodes[index];
                node.loop = true;
                node.positionCount = 6;
                for (int corner = 0; corner < 6; corner++)
                {
                    float radians = (30f + corner * 60f) * Mathf.Deg2Rad;
                    node.SetPosition(corner, nodeCenter + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * PathNodeRadius);
                }
                ShowRing(node, DestinationColor, 0.05f);
            }
            HidePathNodes(nodeCount);
            if (pathArrow == null) return;
            Vector3 destination = centers[Key(option.Destination)];
            Vector3 previous = option.Path.Count > 1
                ? centers[Key(option.Path[option.Path.Count - 2])]
                : origin;
            Vector3 direction = destination - previous;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
            {
                pathArrow.gameObject.SetActive(false);
                return;
            }
            direction.Normalize();
            Vector3 side = new Vector3(-direction.z, 0f, direction.x);
            // Straddle the destination centre so the chevron reads as the last path step.
            Vector3 tip = destination + direction * 0.10f;
            Vector3 back = tip - direction * 0.26f;
            pathArrow.SetPosition(0, back + side * 0.16f);
            pathArrow.SetPosition(1, tip);
            pathArrow.SetPosition(2, back - side * 0.16f);
            pathArrow.gameObject.SetActive(true);
        }

        private void EnsurePathNodes(int count)
        {
            while (pathNodes.Count < count)
            {
                LineRenderer node = CreateLine("MovementPathNode_" + pathNodes.Count, selectedMaterial, 0.05f, DestinationColor);
                node.loop = true;
                node.positionCount = 6;
                pathNodes.Add(node);
            }
        }

        private void HidePathNodes(int from)
        {
            for (int index = from; index < pathNodes.Count; index++)
                pathNodes[index].gameObject.SetActive(false);
        }

        private void ClearSelection()
        {
            if (ownsPreview)
            {
                ownsPreview = false;
                previewView?.PresentMovement(null, null, false, false);
            }
            IsSelecting = false;
            selected = null;
            highlightedDestination = null;
            foreach (LineRenderer ring in highlights.Values)
                if (ring != null)
                {
                    ring.gameObject.SetActive(false);
                    ring.widthMultiplier = ReachableWidth;
                    var properties = new MaterialPropertyBlock();
                    properties.SetColor("_BaseColor", ReachableOutlineColor);
                    ring.SetPropertyBlock(properties);
                }
            if (pathLine != null) pathLine.gameObject.SetActive(false);
            if (pathArrow != null) pathArrow.gameObject.SetActive(false);
            HidePathNodes(0);
            RefreshControls();
        }

        private void PresentMovementPreview(bool pointerOnBoard)
        {
            if (!ownsPreview) return;
            previewView?.PresentMovement(snapshot?.Movement, selected, IsSelecting, pointerOnBoard);
        }

        private void RefreshControls() => actionBar?.PresentMovement(snapshot, IsSelecting, selected);
        private void OnDisable()
        {
            ClearSelection();
            SetInspectionHighlight(null);
        }
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
