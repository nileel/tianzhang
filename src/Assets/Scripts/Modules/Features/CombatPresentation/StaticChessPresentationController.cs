using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class StaticChessPresentationController : MonoBehaviour
    {
        private const float SequenceDuration = 0.32f;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 approvedPosition;
        private StaticChessPresentationEvent activeEvent;
        private float elapsed;
        private bool isPresenting;

        public event Action CastEffectRequested;

        public bool IsPresenting => isPresenting;

        private void Awake()
        {
            CaptureRestPose();
        }

        private void Update()
        {
            if (isPresenting) Tick(Time.deltaTime);
        }

        public void CaptureRestPose()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;
            elapsed = 0f;
            isPresenting = false;
        }

        public void StartPresentation(StaticChessPresentationEvent presentationEvent, Vector3 approvedWorldPosition)
        {
            if (!isPresenting) CaptureRestPose();
            activeEvent = presentationEvent;
            approvedPosition = approvedWorldPosition;
            elapsed = 0f;
            isPresenting = presentationEvent != StaticChessPresentationEvent.Idle;
            if (presentationEvent == StaticChessPresentationEvent.Cast) CastEffectRequested?.Invoke();
            if (!isPresenting) RestoreRoot();
        }

        public void Tick(float deltaTime)
        {
            if (!isPresenting) return;
            elapsed += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(elapsed / SequenceDuration);
            float arc = Mathf.Sin(progress * Mathf.PI);
            switch (activeEvent)
            {
                case StaticChessPresentationEvent.Move:
                    transform.position = Vector3.Lerp(restPosition, approvedPosition, progress) + Vector3.up * (0.12f * arc);
                    break;
                case StaticChessPresentationEvent.Attack:
                    transform.position = restPosition + restRotation * Vector3.forward * (0.1f * arc);
                    transform.rotation = restRotation * Quaternion.Euler(10f * arc, 0f, 0f);
                    break;
                case StaticChessPresentationEvent.Hit:
                    transform.position = restPosition + restRotation * Vector3.right * (0.05f * Mathf.Sin(progress * Mathf.PI * 4f));
                    break;
                case StaticChessPresentationEvent.Cast:
                    transform.position = restPosition + Vector3.up * (0.05f * arc);
                    break;
                case StaticChessPresentationEvent.Death:
                    transform.position = restPosition + Vector3.down * (0.18f * arc);
                    transform.rotation = restRotation * Quaternion.Euler(0f, 0f, 55f * arc);
                    break;
            }
            if (progress >= 1f) RestoreRoot();
        }

        private void RestoreRoot()
        {
            transform.position = restPosition;
            transform.rotation = restRotation;
            elapsed = 0f;
            isPresenting = false;
        }
    }
}
