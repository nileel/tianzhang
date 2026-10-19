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
            Consider(presenter.Snapshot.Enemy?.Id);
            id = closestId;
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

        public string FindUnitAtCell(CombatUnitPresentationHex cell)
        {
            if (presenter?.Snapshot == null) return null;
            Vector3 center = Static3DCombatUnitPresentationProvider.HexToWorld(cell);
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
                if (unit == null || unit.CurrentHealth <= 0 ||
                    !provider.TryGetPresentedUnit(unit.Id, out GameObject presented)) return null;
                Vector3 delta = presented.transform.position - center;
                return delta.x * delta.x + delta.z * delta.z < .0001f ? unit.Id : null;
            }
        }

    }
}
