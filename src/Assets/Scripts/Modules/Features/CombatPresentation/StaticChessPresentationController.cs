using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// 苻渊静态 3D pilot 的唯一表现所有者。它只消费已有表现事件与批准的位置，
    /// 在根节点播放整体位移／旋转、一次性效果和 cue；不写入格位、朝向、规则或存档。
    /// </summary>
    public sealed class StaticChessPresentationController : MonoBehaviour
    {
        public const float StandardEventDuration = 0.32f;
        public const float DeathEventDuration = 0.45f;

        [SerializeField] private GameObject motionEffectPrefab;
        [SerializeField] private AudioSource cueSource;
        [SerializeField] private AudioClip moveCue;
        [SerializeField] private AudioClip attackCue;
        [SerializeField] private AudioClip hitCue;
        [SerializeField] private AudioClip castCue;
        [SerializeField] private AudioClip deathCue;

        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 approvedPosition;
        private CombatUnitPresentationEvent activeEvent;
        private float elapsed;
        private bool isPresenting;
        private bool keySignalRaised;
        private GameObject activeEffect;

        public event Action CastEffectRequested;

        public event Action<CombatUnitPresentationEvent> MotionEffectRequested;

        public bool IsPresenting => isPresenting;

        public CombatUnitPresentationEvent ActiveEvent => activeEvent;

        public float ActiveDuration => DurationFor(activeEvent);

        public float PresentationProgress => isPresenting
            ? Mathf.Clamp01(elapsed / ActiveDuration)
            : 0f;

        public int EffectPlayCount { get; private set; }

        public int CuePlayCount { get; private set; }

        public AudioClip LastPlayedCue { get; private set; }

        public GameObject ActiveEffect => activeEffect;

        private void Awake()
        {
            CaptureRestPose();
        }

        private void OnDisable()
        {
            ClearActiveEffect();
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
            keySignalRaised = false;
        }

        public void StartPresentation(CombatUnitPresentationEvent presentationEvent, Vector3 approvedWorldPosition)
        {
            ValidateConfiguration();
            ClearActiveEffect();
            if (!isPresenting) CaptureRestPose();
            activeEvent = presentationEvent;
            approvedPosition = approvedWorldPosition;
            elapsed = 0f;
            keySignalRaised = false;
            EffectPlayCount = 0;
            CuePlayCount = 0;
            LastPlayedCue = null;
            isPresenting = presentationEvent != CombatUnitPresentationEvent.Idle;
            if (!isPresenting) RestoreRoot();
        }

        public void Tick(float deltaTime)
        {
            if (!isPresenting) return;

            float duration = ActiveDuration;
            float previousProgress = Mathf.Clamp01(elapsed / duration);
            elapsed += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(elapsed / duration);
            ApplyRootPresentation(progress);

            float keyProgress = KeyProgressFor(activeEvent);
            if (!keySignalRaised && previousProgress <= keyProgress && progress + 0.0001f >= keyProgress)
            {
                keySignalRaised = true;
                PlayKeySignal();
            }

            if (progress >= 1f) RestoreRoot();
        }

        public static float DurationFor(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Idle: return 0f;
                case CombatUnitPresentationEvent.Death: return DeathEventDuration;
                case CombatUnitPresentationEvent.Move:
                case CombatUnitPresentationEvent.Attack:
                case CombatUnitPresentationEvent.Hit:
                case CombatUnitPresentationEvent.Cast:
                    return StandardEventDuration;
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }

        public static float KeyProgressFor(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Idle: return 0f;
                case CombatUnitPresentationEvent.Move: return 0.85f;
                case CombatUnitPresentationEvent.Attack: return 0.55f;
                case CombatUnitPresentationEvent.Hit: return 0.10f;
                case CombatUnitPresentationEvent.Cast: return 0.20f;
                case CombatUnitPresentationEvent.Death: return 0.70f;
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }

        private void ApplyRootPresentation(float progress)
        {
            switch (activeEvent)
            {
                case CombatUnitPresentationEvent.Move:
                    ApplyMove(progress);
                    break;
                case CombatUnitPresentationEvent.Attack:
                    ApplyAttack(progress);
                    break;
                case CombatUnitPresentationEvent.Hit:
                    ApplyHit(progress);
                    break;
                case CombatUnitPresentationEvent.Cast:
                    ApplyCast(progress);
                    break;
                case CombatUnitPresentationEvent.Death:
                    ApplyDeath(progress);
                    break;
            }
        }

        private void ApplyMove(float progress)
        {
            float translation = Mathf.InverseLerp(0.15f, 0.85f, progress);
            float lift = progress < 0.15f
                ? Mathf.InverseLerp(0f, 0.15f, progress)
                : progress > 0.85f ? 1f - Mathf.InverseLerp(0.85f, 1f, progress) : 1f;
            transform.position = Vector3.Lerp(restPosition, approvedPosition, translation) + Vector3.up * (0.12f * lift);
            transform.rotation = restRotation;
        }

        private void ApplyAttack(float progress)
        {
            float advance = progress < 0.20f ? 0f : progress <= 0.55f
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.55f, progress))
                : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, progress));
            transform.position = restPosition + restRotation * Vector3.forward * (0.10f * advance);
            transform.rotation = restRotation * Quaternion.Euler(10f * advance, 0f, 0f);
        }

        private void ApplyHit(float progress)
        {
            float reaction = progress < 0.10f ? 0f : progress <= 0.55f
                ? Mathf.Sin(Mathf.InverseLerp(0.10f, 0.55f, progress) * Mathf.PI)
                : 1f - Mathf.InverseLerp(0.55f, 1f, progress);
            transform.position = restPosition + restRotation *
                (Vector3.right * (0.05f * reaction) + Vector3.back * (0.035f * reaction));
            transform.rotation = restRotation * Quaternion.Euler(-5f * reaction, 0f, -3f * reaction);
        }

        private void ApplyCast(float progress)
        {
            float lift = progress < 0.20f
                ? Mathf.InverseLerp(0f, 0.20f, progress)
                : progress > 0.70f ? 1f - Mathf.InverseLerp(0.70f, 1f, progress) : 1f;
            transform.position = restPosition + Vector3.up * (0.05f * lift);
            transform.rotation = restRotation;
        }

        private void ApplyDeath(float progress)
        {
            float fall = progress < 0.25f
                ? Mathf.InverseLerp(0f, 0.25f, progress)
                : progress > 0.70f ? 1f - Mathf.InverseLerp(0.70f, 1f, progress) : 1f;
            transform.position = restPosition + Vector3.down * (0.18f * fall);
            transform.rotation = restRotation * Quaternion.Euler(0f, 0f, 55f * fall);
        }

        private void PlayKeySignal()
        {
            AudioClip cue = CueFor(activeEvent);
            if (cue == null) throw new InvalidOperationException("Static chess motion cue is missing for " + activeEvent + ".");
            if (motionEffectPrefab == null) throw new InvalidOperationException("Static chess motion effect prefab is missing.");
            if (cueSource == null) throw new InvalidOperationException("Static chess motion cue source is missing.");

            Vector3 effectPosition;
            Color effectColor;
            switch (activeEvent)
            {
                case CombatUnitPresentationEvent.Move:
                    effectPosition = transform.position - Vector3.up * 0.12f;
                    effectColor = new Color(0.92f, 0.72f, 0.30f, 1f);
                    break;
                case CombatUnitPresentationEvent.Attack:
                    effectPosition = transform.position + transform.rotation * Vector3.forward * 0.24f + Vector3.up * 0.42f;
                    effectColor = new Color(1f, 0.58f, 0.24f, 1f);
                    break;
                case CombatUnitPresentationEvent.Hit:
                    effectPosition = transform.position + Vector3.up * 0.48f;
                    effectColor = new Color(1f, 0.38f, 0.20f, 1f);
                    break;
                case CombatUnitPresentationEvent.Cast:
                    effectPosition = transform.position + Vector3.up * 0.32f;
                    effectColor = new Color(0.46f, 0.78f, 1f, 1f);
                    break;
                case CombatUnitPresentationEvent.Death:
                    effectPosition = transform.position - Vector3.up * 0.16f;
                    effectColor = new Color(0.62f, 0.66f, 0.72f, 1f);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            activeEffect = Instantiate(motionEffectPrefab, effectPosition, Quaternion.identity);
            ParticleSystem particles = activeEffect.GetComponentInChildren<ParticleSystem>(true);
            if (particles == null) throw new InvalidOperationException("Static chess motion effect prefab has no ParticleSystem.");
            ParticleSystem.MainModule main = particles.main;
            main.startColor = effectColor;
            particles.Emit(12);
            particles.Play(true);
            EffectPlayCount++;
            MotionEffectRequested?.Invoke(activeEvent);

            cueSource.PlayOneShot(cue);
            CuePlayCount++;
            LastPlayedCue = cue;
            if (activeEvent == CombatUnitPresentationEvent.Cast) CastEffectRequested?.Invoke();
        }

        private AudioClip CueFor(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Move: return moveCue;
                case CombatUnitPresentationEvent.Attack: return attackCue;
                case CombatUnitPresentationEvent.Hit: return hitCue;
                case CombatUnitPresentationEvent.Cast: return castCue;
                case CombatUnitPresentationEvent.Death: return deathCue;
                case CombatUnitPresentationEvent.Idle: return null;
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }

        private void RestoreRoot()
        {
            transform.position = restPosition;
            transform.rotation = restRotation;
            elapsed = 0f;
            isPresenting = false;
            activeEvent = CombatUnitPresentationEvent.Idle;
        }

        private void ClearActiveEffect()
        {
            if (activeEffect == null) return;
            Destroy(activeEffect);
            activeEffect = null;
        }

        private void ValidateConfiguration()
        {
            if (motionEffectPrefab == null || cueSource == null || moveCue == null || attackCue == null ||
                hitCue == null || castCue == null || deathCue == null)
                throw new InvalidOperationException("Static chess motion presentation is missing a frozen effect, cue source, or cue.");
        }
    }
}
