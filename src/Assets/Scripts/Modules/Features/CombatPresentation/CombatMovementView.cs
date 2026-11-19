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
        // Cool movement range / selectable spell targets, warm destination, and amber effect cells.
        private static readonly Color ReachableOutlineColor = new Color32(0x23, 0x8C, 0xFF, 0xFF);
        private static readonly Color DestinationColor = new Color32(0xF2, 0xC1, 0x4E, 0xFF);
        private static readonly Color EffectCellColor = new Color32(0xFF, 0x9F, 0x3C, 0xFF);
        // Ground rings identify the full committed footprint of the inspected unit.
        private static readonly Color InspectionColor = new Color32(0xFF, 0xC8, 0x5C, 0xFF);
        private const float ReachableWidth = 0.035f;
        private const float DestinationWidth = 0.07f;
        private const float EffectWidth = 0.06f;
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
        private CombatBattlefieldSurface surface;
        public void UseSurface(CombatBattlefieldSurface value)
        {
            ClearSelection();
            surface = value;
            if (surface == null) throw new ArgumentNullException(nameof(value));
            cells = new Cell[surface.Cells.Count];
            for (int i=0;i<cells.Length;i++)
            {
                var c=surface.Cells[i];
                cells[i]=new Cell { q=c.q,r=c.r,tile=surface.transform.Find("GuanzhongHex_"+c.q+"_"+c.r) };
            }
            BuildCellHighlights();
            inspectionPose = null;
        }
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
        private CombatUnitPresentationPose? inspectionPose;
        private LineRenderer[] inspectionRings;
        private readonly List<Vector2Int> destinationCells = new List<Vector2Int>(2);
        private readonly List<Vector3> pathPoints = new List<Vector3>();
        private bool ownsPreview;
        public Action SelectionStarted { private get; set; }
        public bool IsSelecting { get; private set; }
        public bool IsLocked { get; private set; }
        public CombatMovementOption Selected => selected;
        public IReadOnlyDictionary<Vector2Int, LineRenderer> Highlights => highlights;
        public LineRenderer InspectionRing => inspectionRing;
        public IReadOnlyList<LineRenderer> InspectionRings => inspectionRings ?? Array.Empty<LineRenderer>();
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
            BuildCellHighlights();
            pathLine = CreateLine("MovementPathPreview", selectedMaterial, 0.065f, DestinationColor);
            pathLine.gameObject.SetActive(false);
            pathArrow = CreateLine("MovementPathDirection", selectedMaterial, 0.07f, DestinationColor);
            pathArrow.positionCount = 3;
            pathArrow.gameObject.SetActive(false);
            inspectionRing = CreateLine("InspectionCellRing", selectedMaterial, InspectionWidth, InspectionColor);
            inspectionRing.loop = true;
            inspectionRing.positionCount = 6;
            inspectionRing.gameObject.SetActive(false);
            LineRenderer rearRing = CreateLine("InspectionRearCellRing", selectedMaterial, InspectionWidth, InspectionColor);
            rearRing.loop = true;
            rearRing.positionCount = 6;
            rearRing.gameObject.SetActive(false);
            inspectionRings = new[] { inspectionRing, rearRing };
        }

        private void BuildCellHighlights()
        {
            foreach (var ring in highlights.Values) if (ring != null) Destroy(ring.gameObject);
            highlights.Clear();
            centers.Clear();
            foreach (Cell cell in cells)
            {
                if (cell.tile == null) throw new InvalidOperationException("combat_movement_tile_missing");
                Vector3 center = cell.tile.position;
                center.y = (surface != null ? surface.Center(new CombatUnitPresentationHex(cell.q,cell.r)).y : TileBodyTop(cell.tile, center.y)) + SelectionBorderOffset;
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
                if (surface != null) surface.DrawRing(ring,new CombatUnitPresentationHex(cell.q,cell.r),.92f/Mathf.Sqrt(3),SelectionBorderOffset);
                ring.gameObject.SetActive(false);
                highlights.Add(key, ring);
            }
        }

        /// <summary>Compatibility entry for the single-cell player and existing UI callers.</summary>
        public void SetInspectionHighlight(CombatUnitPresentationHex? cell) =>
            SetInspectionPose(cell.HasValue ? new CombatUnitPresentationPose(cell.Value, 0) : (CombatUnitPresentationPose?)null);

        public void SetInspectionPose(CombatUnitPresentationPose? pose)
        {
            if (inspectionRings == null) return;
            if (Nullable.Equals(inspectionPose, pose)) return;
            inspectionPose = pose;
            inspectionCell = pose?.Position;
            for (int index = 0; index < inspectionRings.Length; index++)
            {
                LineRenderer ring = inspectionRings[index];
                if (!pose.HasValue || index == 1 && !pose.Value.IsDoubleCell ||
                    !centers.TryGetValue(Key(index == 0 ? pose.Value.Position : pose.Value.Rear), out Vector3 center))
                { ring.gameObject.SetActive(false); continue; }
                for (int corner = 0; corner < 6; corner++)
                {
                    float radians = (30f + corner * 60f) * Mathf.Deg2Rad;
                    ring.SetPosition(corner,
                        center + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * (0.92f / Mathf.Sqrt(3f)));
                }
                ShowRing(ring, InspectionColor, InspectionWidth);
                if (surface != null) surface.DrawRing(ring,index==0 ? pose.Value.Position : pose.Value.Rear,.92f/Mathf.Sqrt(3),SelectionBorderOffset);
            }
        }

        public void Present(CombatHudSnapshot value)
        {
            // Every new authority projection invalidates any uncommitted candidate.
            snapshot = value;
            ClearSelection();
        }

        /// <summary>Shows legal spell targets, then overlays the current effect's actual footprint.</summary>
        public void PresentAttackArea(CombatAttackAreaSnapshot area,
            IReadOnlyList<CombatUnitPresentationHex> targetCells = null,
            IReadOnlyList<CombatUnitPresentationHex> selectableCells = null)
        {
            ClearSelection();
            if (!CanInput) return;
            if (selectableCells != null)
                foreach (CombatUnitPresentationHex cell in selectableCells)
                    if (highlights.TryGetValue(Key(cell), out LineRenderer ring))
                        ShowRing(ring, ReachableOutlineColor, ReachableWidth);
            if (area != null)
            {
                foreach (CombatAttackAreaCellSnapshot cell in area.Cells)
                {
                    if (cell.IsLegal && highlights.TryGetValue(Key(cell.Position), out LineRenderer ring))
                        ShowRing(ring, EffectCellColor, EffectWidth);
                }
                return;
            }
            if (targetCells != null)
                foreach (CombatUnitPresentationHex cell in targetCells)
                    if (highlights.TryGetValue(Key(cell), out LineRenderer ring))
                        ShowRing(ring, EffectCellColor, EffectWidth);
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

        public void BeginSelection()
        {
            if (!CanInput || snapshot.Movement == null || snapshot.Movement.Options.Count == 0) return;
            SelectionStarted?.Invoke();
            IsSelecting = true;
            selected = null;
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
            if (!CanInput || !IsLocked || selected?.Candidate == null) return;
            CombatMovementOption committed = selected;
            // Close the popup and range before the authority projection can repaint either.
            ClearSelection();
            previewHandler.ConfirmAction(committed.Candidate);
        }

        public void CancelSelectionLayer()
        {
            if (!IsLocked) { Cancel(); return; }
            IsLocked = false;
            selected = null;
            HighlightDestination(null);
            DrawPath(null);
            PresentMovementPreview(false);
            RefreshControls();
        }

        private void LateUpdate()
        {
            if (!CanInput || !IsSelecting || IsLocked || snapshot.Movement == null) return;
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pointer = mouse.position.ReadValue();
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
            IsLocked = mouse.leftButton.wasPressedThisFrame && hovered != null;
            HighlightDestination(hovered);
            DrawPath(hovered);
            PresentMovementPreview(pointerOnBoard);
            actionBar?.PresentMovement(snapshot, IsSelecting, hovered);
        }

        public bool TryPickCell(Vector2 screenPoint, out CombatUnitPresentationHex picked)
        {
            picked = default;
            if (battlefieldCamera == null || cells.Length == 0 || UiOwnsPointer(screenPoint)) return false;
            Ray ray = battlefieldCamera.ScreenPointToRay(screenPoint);
            if (surface != null) return surface.TryPick(ray,out picked);
            var plane = new Plane(Vector3.up, cells[0].tile.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            return TryGetCellAtWorld(ray.GetPoint(distance), out picked);
        }

        public bool TryGetCellAtWorld(Vector3 position, out CombatUnitPresentationHex picked)
        {
            if (surface != null) return surface.TryCell(position,out picked);
            picked = default;
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

        public bool UiOwnsPointer(Vector2 pointer)
        {
            EventSystem system = EventSystem.current;
            if (system == null) return false;
            uiHits.Clear();
            system.RaycastAll(new PointerEventData(system) { position = pointer }, uiHits);
            return uiHits.Count > 0;
        }

        /// <summary>The selected destination displays both cells when its quoted final pose occupies two.</summary>
        private void HighlightDestination(CombatMovementOption hovered)
        {
            foreach (Vector2Int key in destinationCells)
            {
                if (!highlights.TryGetValue(key, out LineRenderer previous)) continue;
                bool reachable = false;
                foreach (CombatMovementOption option in snapshot.Movement.Options)
                    if (Key(option.Destination) == key) { reachable = true; break; }
                previous.gameObject.SetActive(reachable);
                if (reachable) ShowRing(previous, ReachableOutlineColor, ReachableWidth);
            }
            destinationCells.Clear();
            if (hovered == null) return;
            if (hovered.MovementSteps.Count > 0)
            {
                CombatUnitPresentationPose pose = hovered.MovementSteps[hovered.MovementSteps.Count - 1].To;
                Add(pose.Position);
                if (pose.IsDoubleCell) Add(pose.Rear);
            }
            else Add(hovered.Destination);

            void Add(CombatUnitPresentationHex cell)
            {
                Vector2Int key = Key(cell);
                if (!highlights.TryGetValue(key, out LineRenderer ring)) return;
                destinationCells.Add(key);
                ShowRing(ring, DestinationColor, DestinationWidth);
            }
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
            pathPoints.Clear();
            bool flying = option.MovementSteps.Count > 0 && option.MovementSteps[0].From.AltitudeBand > 0;
            if (flying)
            {
                pathPoints.Add(CombatUnitFlightVisual.Position(option.MovementSteps[0].From, surface));
                foreach (var step in option.MovementSteps) pathPoints.Add(CombatUnitFlightVisual.Position(step.To, surface));
            }
            else if (option.MovementSteps.Count > 0 && option.MovementSteps[0].From.IsDoubleCell)
            {
                pathPoints.Add(PoseCenter(option.MovementSteps[0].From));
                foreach (CombatUnitPresentationMovementStep step in option.MovementSteps)
                {
                    foreach (int facing in step.TurnFacings)
                        pathPoints.Add(PoseCenter(new CombatUnitPresentationPose(step.From.Position, facing, true)));
                    pathPoints.Add(PoseCenter(step.To));
                }
            }
            else
            {
                pathPoints.Add(centers[Key(snapshot.Movement.Origin)]);
                foreach (CombatUnitPresentationHex cell in option.Path) pathPoints.Add(centers[Key(cell)]);
            }
            if (surface != null && !flying) surface.DrawPath(pathLine,pathPoints,SelectionBorderOffset);
            else
            {
                pathLine.positionCount = pathPoints.Count;
                for (int index = 0; index < pathPoints.Count; index++) pathLine.SetPosition(index, pathPoints[index]);
            }
            int nodeCount = Math.Max(0, pathPoints.Count - 2);
            EnsurePathNodes(nodeCount);
            for (int index = 0; index < nodeCount; index++)
            {
                Vector3 nodeCenter = pathPoints[index + 1];
                LineRenderer node = pathNodes[index];
                node.loop = true;
                node.positionCount = 6;
                for (int corner = 0; corner < 6; corner++)
                {
                    float radians = (30f + corner * 60f) * Mathf.Deg2Rad;
                    Vector3 p=nodeCenter + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * PathNodeRadius;
                    if(surface != null && !flying && surface.TryHeight(p,out float y)) p.y=y+SelectionBorderOffset;
                    node.SetPosition(corner,p);
                }
                ShowRing(node, DestinationColor, 0.05f);
            }
            HidePathNodes(nodeCount);
            if (pathArrow == null) return;
            Vector3 destination = pathPoints[pathPoints.Count - 1];
            Vector3 previous = pathPoints.Count > 1 ? pathPoints[pathPoints.Count - 2] : destination;
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

        private Vector3 PoseCenter(CombatUnitPresentationPose pose) => pose.IsDoubleCell
            ? (centers[Key(pose.Position)] + centers[Key(pose.Rear)]) * .5f : centers[Key(pose.Position)];

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
            IsLocked = false;
            selected = null;
            destinationCells.Clear();
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
            previewView?.PresentMovement(snapshot?.Movement, selected, IsSelecting, pointerOnBoard, IsLocked);
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
