using System;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatTargetInput : MonoBehaviour
    {
        private Camera worldCamera;
        private Static3DCombatUnitPresentationProvider provider;
        private CombatHudPresenter presenter;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        public void Configure(Camera camera, Static3DCombatUnitPresentationProvider units, CombatHudPresenter hud)
        {
            worldCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            provider = units != null ? units : throw new ArgumentNullException(nameof(units));
            presenter = hud != null ? hud : throw new ArgumentNullException(nameof(hud));
        }

        private void Update()
        {
            if (presenter == null || !presenter.CanInspect || Mouse.current == null ||
                !Mouse.current.leftButton.wasPressedThisFrame) return;
            Vector2 point = Mouse.current.position.ReadValue();
            if (EventSystem.current != null)
            {
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, uiHits);
                if (uiHits.Count > 0) return;
            }
            presenter.InspectTarget(TryPick(point, out string id) ? id : null);
        }

        public bool TryPick(Vector2 screenPoint, out string id)
        {
            id = null;
            if (worldCamera == null || presenter?.Snapshot == null) return false;
            Ray ray = worldCamera.ScreenPointToRay(screenPoint);
            float nearest = float.PositiveInfinity;
            string closestId = null;
            Consider(presenter.Snapshot.Player?.Id);
            // Every living enemy must be pickable, not only the primary one.
            foreach (TianZhang.Gameplay.Contracts.CombatantHudSnapshot enemy in presenter.Snapshot.Enemies)
                Consider(enemy?.Id);
            id = closestId;
            if (id == null && TryGetCell(screenPoint, out CombatUnitPresentationHex cell))
                id = FindUnitAtCell(cell);
            return id != null;

            void Consider(string candidate)
            {
                if (presenter.FindInspectable(candidate) == null || !provider.TryGetVisibleBounds(candidate, out Bounds bounds)) return;
                if (bounds.IntersectRay(ray, out float distance) && distance < nearest)
                {
                    nearest = distance;
                    closestId = candidate;
                }
            }
        }

        /// <summary>Board cell under the pointer; a cell click also selects the unit standing there.</summary>
        public bool TryGetCell(Vector2 screenPoint, out CombatUnitPresentationHex cell)
        {
            cell = default;
            if (worldCamera == null) return false;
            Ray ray = worldCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, new Vector3(0f, Static3DCombatUnitPresentationProvider.HexToWorld(
                new CombatUnitPresentationHex(0, 0)).y, 0f));
            if (!plane.Raycast(ray, out float distance)) return false;
            Vector3 position = ray.GetPoint(distance);
            // Inverse of the formal axial grid used by the board and the unit carrier.
            int r = Mathf.RoundToInt((position.z - 1f) / 0.8660254f);
            int q = Mathf.RoundToInt(position.x - r * 0.5f);
            var candidate = new CombatUnitPresentationHex(q, r);
            Vector3 delta = position - Static3DCombatUnitPresentationProvider.HexToWorld(candidate);
            if (Mathf.Abs(delta.x) > 0.5f ||
                Mathf.Abs(0.5f * delta.x + 0.8660254f * delta.z) > 0.5f ||
                Mathf.Abs(0.5f * delta.x - 0.8660254f * delta.z) > 0.5f)
                return false;
            cell = candidate;
            return true;
        }

        public string FindUnitAtCell(CombatUnitPresentationHex cell)
        {
            if (presenter?.Snapshot == null) return null;
            string player = Find(presenter.Snapshot.Player);
            if (player != null) return player;
            foreach (var enemy in presenter.Snapshot.Enemies)
            {
                string id = Find(enemy);
                if (id != null) return id;
            }
            return null;

            string Find(TianZhang.Gameplay.Contracts.CombatantHudSnapshot unit)
            {
                if (unit == null || unit.CurrentHealth <= 0 || provider == null ||
                    !provider.TryGetPresentedPose(unit.Id, out CombatUnitPresentationPose pose)) return null;
                return pose.Occupies(cell) ? unit.Id : null;
            }
        }

        public bool TryGetUnitPose(string id, out CombatUnitPresentationPose pose)
        {
            pose = default;
            return provider != null && !string.IsNullOrWhiteSpace(id) && provider.TryGetPresentedPose(id, out pose);
        }

        public bool TryPickBodyPoint(Vector2 screenPoint, out Vector3 point)
        {
            point = default;
            return worldCamera != null && provider != null &&
                provider.TryPickBodyPoint(worldCamera.ScreenPointToRay(screenPoint), out point);
        }

        /// <summary>Front cell of an already spawned unit, read from the presentation that mirrors authority.</summary>
        public bool TryGetUnitCell(string id, out CombatUnitPresentationHex cell)
        {
            cell = default;
            return provider != null && !string.IsNullOrWhiteSpace(id) &&
                provider.TryGetPresentedPosition(id, out cell);
        }

    }
}
