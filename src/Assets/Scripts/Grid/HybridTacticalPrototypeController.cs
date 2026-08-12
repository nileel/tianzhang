using System.Collections.Generic;
using System.Linq;
using TianZhang.Content;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.InputSystem;

using TianZhang.Spatial;

namespace TianZhang.Tactical
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HybridTacticalRenderer))]
    public sealed class HybridTacticalPrototypeController : MonoBehaviour
    {
        [SerializeField] private HybridTacticalRenderer hybridRenderer;
        [SerializeField] private Camera presentationCamera;
        [SerializeField] private EnvironmentProfileAsset environmentProfile;
        [SerializeField, Range(5, 7)] private int prototypeRadius = 5;

        public TacticalGridModel Model { get; private set; }
        public SpatialQuerySnapshot SpatialQuery { get; private set; }
        public HexCoord SelectedHex { get; private set; }
        public int PrototypeRadius => prototypeRadius;
        public EnvironmentProfileAsset EnvironmentProfile => environmentProfile;

        public void SetEnvironmentProfile(EnvironmentProfileAsset profile)
        {
            environmentProfile = profile;
        }

        public void SetPresentationCamera(Camera camera)
        {
            presentationCamera = camera;
            if (hybridRenderer != null)
                hybridRenderer.SetPresentationCamera(camera);
        }

        public void SetPrototypeRadius(int radius)
        {
            prototypeRadius = Mathf.Clamp(radius, 5, 7);
        }

        public bool Initialize(TacticalGridModel model, EnvironmentProfileAsset profile, out string reason)
        {
            hybridRenderer = hybridRenderer != null ? hybridRenderer : GetComponent<HybridTacticalRenderer>();
            if (hybridRenderer == null)
            {
                reason = "hybrid_renderer_not_configured";
                return false;
            }

            if (presentationCamera != null)
                hybridRenderer.SetPresentationCamera(presentationCamera);

            if (!SpatialQueryBoardFactory.TryCreate(model, profile, out var snapshot, out reason))
                return false;

            Model = model;
            SpatialQuery = snapshot;
            environmentProfile = profile;
            hybridRenderer.RenderGrid(model);
            hybridRenderer.PresentEnvironment(model, snapshot.Environment);
            SelectedHex = FindSelectionAnchor(model);
            RefreshPresentation();
            return true;
        }

        public bool TrySelectFromRay(Ray ray, out HexCoord selected)
        {
            selected = default;
            if (hybridRenderer == null || SpatialQuery == null || !hybridRenderer.TryRaycastToHex(ray, out selected))
                return false;

            SelectedHex = selected;
            RefreshPresentation();
            return true;
        }

        private void Start()
        {
            if (Initialize(BuildPrototypeGrid(prototypeRadius), environmentProfile, out var reason))
                return;

            Debug.LogError("[HybridTacticalPrototype] Spatial query setup failed: " + reason);
            enabled = false;
        }

        private void Update()
        {
            if (!Application.isPlaying || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;

            var camera = presentationCamera != null ? presentationCamera : Camera.main;
            if (camera == null)
                return;

            TrySelectFromRay(camera.ScreenPointToRay(Mouse.current.position.ReadValue()), out _);
        }

        private void RefreshPresentation()
        {
            if (hybridRenderer == null || SpatialQuery == null)
                return;

            var anchor = FindSelectionAnchor(Model);
            var spatialAnchor = anchor;
            var occupied = SpatialQuery.Occupied;
            var movement = SpatialQuery.Board.FindReachable(spatialAnchor, 1, occupied)
                .Keys
                .ToArray();
            var attack = SpatialQuery.Board.Cells
                .Where(coord => SpatialQuery.Board.QueryRangeEntry(
                    spatialAnchor,
                    coord,
                    1,
                    1,
                    SpatialQueryKind.Attack,
                    true).IsInRange)
                .ToArray();

            hybridRenderer.ClearOverlay();
            hybridRenderer.HighlightMoveRange(movement);
            hybridRenderer.HighlightAttackRange(attack);
            hybridRenderer.HighlightSelected(SelectedHex);
        }

        private static TacticalGridModel BuildPrototypeGrid(int radius)
        {
            var model = new TacticalGridModel();
            for (int q = -radius; q <= radius; q++)
            {
                for (int r = Mathf.Max(-radius, -q - radius); r <= Mathf.Min(radius, -q + radius); r++)
                {
                    var coord = new HexCoord(q, r);
                    int heightLevel = q + r >= 3 ? 1 : 0;
                    bool blocked = q == radius && r == 0;
                    model.SetTile(new TacticalTileData(coord)
                    {
                        TerrainType = blocked
                            ? TacticalTerrainType.Obstacle
                            : heightLevel > 0 ? TacticalTerrainType.HighGround : TacticalTerrainType.Plain,
                        HeightLevel = heightLevel,
                        BlocksGroundMove = blocked,
                        BlocksLanding = blocked,
                    });
                }
            }

            model.SetOccupied(new HexCoord(0, 0), 1);
            return model;
        }

        private static HexCoord FindSelectionAnchor(TacticalGridModel model)
        {
            if (model != null)
            {
                foreach (var tile in model.Tiles)
                {
                    if (tile.IsOccupied)
                        return tile.Coord;
                }
            }

            return new HexCoord(0, 0);
        }

    }
}
