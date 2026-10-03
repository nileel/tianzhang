using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TianZhang.Features.Adventure
{
    /// <summary>Moves the Adventure camera on the battlefield plane without owning combat input or state.</summary>
    public sealed class AdventureCameraController : MonoBehaviour
    {
        [SerializeField] private Camera controlledCamera;
        [SerializeField] private Transform battlefield;
        [SerializeField] private float groundHeight;
        [SerializeField] private float panSpeed = 4f;

        private Bounds battlefieldBounds;
        private bool hasBounds;
        private bool dragging;
        private Vector2 previousPointerPosition;

        public void Configure(Camera camera, Transform battlefieldRoot, float planeHeight, float speed = 4f)
        {
            controlledCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            battlefield = battlefieldRoot != null ? battlefieldRoot : throw new ArgumentNullException(nameof(battlefieldRoot));
            groundHeight = planeHeight;
            panSpeed = speed;
            hasBounds = false;
            dragging = false;
        }

        private void OnEnable()
        {
            hasBounds = false;
            dragging = false;
        }

        private void OnDisable() => dragging = false;

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) dragging = false;
        }

        private void LateUpdate()
        {
            if (!hasBounds && !ReadBattlefieldBounds()) return;
            Mouse mouse = Mouse.current;
            if (UiOwnsInput(mouse))
            {
                dragging = false;
                return;
            }

            Vector3 displacement = Vector3.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) -
                                   (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) -
                                 (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                Vector2 input = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
                Vector3 right = Vector3.ProjectOnPlane(controlledCamera.transform.right, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(controlledCamera.transform.up, Vector3.up).normalized;
                displacement = (right * input.x + forward * input.y) * (panSpeed * Time.unscaledDeltaTime);
            }

            if (mouse != null && mouse.middleButton.isPressed)
            {
                Vector2 pointer = mouse.position.ReadValue();
                if (mouse.middleButton.wasPressedThisFrame)
                {
                    dragging = true;
                    previousPointerPosition = pointer;
                }
                if (dragging)
                {
                    if (TryProjectToGround(previousPointerPosition, out Vector3 previous) &&
                        TryProjectToGround(pointer, out Vector3 current))
                        displacement += previous - current;
                    previousPointerPosition = pointer;
                }
            }
            else
            {
                dragging = false;
            }

            if (!TryProjectToGround(controlledCamera.pixelRect.center, out Vector3 focus)) return;
            Vector3 desired = focus + displacement;
            desired.x = Mathf.Clamp(desired.x, battlefieldBounds.min.x, battlefieldBounds.max.x);
            desired.z = Mathf.Clamp(desired.z, battlefieldBounds.min.z, battlefieldBounds.max.z);
            Vector3 translation = desired - focus;
            translation.y = 0f;
            controlledCamera.transform.position += translation;
        }

        private bool ReadBattlefieldBounds()
        {
            if (controlledCamera == null || battlefield == null)
            {
                Debug.LogError("Adventure camera requires its configured camera and battlefield.", this);
                enabled = false;
                return false;
            }
            foreach (Renderer renderer in battlefield.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!hasBounds) battlefieldBounds = renderer.bounds;
                else battlefieldBounds.Encapsulate(renderer.bounds);
                hasBounds = true;
            }
            if (!hasBounds)
            {
                Debug.LogError("Adventure camera battlefield has no visible geometry bounds.", this);
                enabled = false;
            }
            return hasBounds;
        }

        private bool TryProjectToGround(Vector2 screenPoint, out Vector3 point)
        {
            Ray ray = controlledCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }
            point = default;
            return false;
        }

        private static bool UiOwnsInput(Mouse mouse)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            GameObject selected = eventSystem.currentSelectedGameObject;
            InputField input = selected != null ? selected.GetComponentInParent<InputField>() : null;
            if (input != null && input.isFocused) return true;
            return mouse != null && eventSystem.IsPointerOverGameObject(mouse.deviceId);
        }
    }
}
