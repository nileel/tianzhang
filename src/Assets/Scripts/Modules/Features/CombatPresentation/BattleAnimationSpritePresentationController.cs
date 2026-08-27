using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// 苻渊 2D 战斗动画 pilot 的受限表现控制器。它只读取既有表现事件和已冻结的
    /// raster atlas，不拥有角色朝向、格位、伤害、状态、结算或存档；缺帧即失败关闭，
    /// 不回退到旧静态 2D 样张。每状态均为六方向、每方向三帧，并在结束时恢复根节点。
    /// </summary>
    public sealed class BattleAnimationSpritePresentationController : MonoBehaviour
    {
        public const int DirectionCount = 6;
        public const int FramesPerDirection = 3;
        public const float FrameDuration = 0.1f;

        [SerializeField] private Sprite[] idleFrames = new Sprite[DirectionCount * FramesPerDirection];
        [SerializeField] private Sprite[] moveFrames = new Sprite[DirectionCount * FramesPerDirection];
        [SerializeField] private Sprite[] attackFrames = new Sprite[DirectionCount * FramesPerDirection];
        [SerializeField] private Sprite[] hitFrames = new Sprite[DirectionCount * FramesPerDirection];
        [SerializeField] private Sprite[] castFrames = new Sprite[DirectionCount * FramesPerDirection];
        [SerializeField] private Sprite[] deathFrames = new Sprite[DirectionCount * FramesPerDirection];
        [SerializeField] private int activeDirection;

        private SpriteRenderer body;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 approvedPosition;
        private CombatUnitPresentationEvent activeEvent;
        private float elapsed;
        private int activeFrameIndex;
        private bool isPresenting;
        private bool castEffectRaised;
        private bool rootPresentationEnabled;

        public event Action CastEffectRequested;

        public bool IsPresenting => isPresenting;

        public bool RootPresentationEnabled => rootPresentationEnabled;

        public int ActiveDirection => activeDirection;

        public int ActiveFrameIndex => activeFrameIndex;

        public CombatUnitPresentationEvent ActiveEvent => activeEvent;

        public string ActiveSpriteName => body != null && body.sprite != null ? body.sprite.name : null;

        public SpriteRenderer Body => body;

        public int EventFrameIndex => EventFrameIndexFor(activeEvent);

        private void Awake()
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length != 1)
                throw new InvalidOperationException("Battle animation sprite prefab must contain exactly one SpriteRenderer.");
            body = renderers[0];
            ValidateFrames();
            CaptureRestPose();
            SetFrame(CombatUnitPresentationEvent.Idle, activeDirection, 0);
        }

        private void Update()
        {
            if (isPresenting) Tick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (body == null) return;
            Camera camera = Camera.main;
            if (camera != null) body.transform.rotation = camera.transform.rotation;
        }

        public void CaptureRestPose()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;
            elapsed = 0f;
            isPresenting = false;
            castEffectRaised = false;
        }

        public void SetDirection(int direction)
        {
            if (direction < 0 || direction >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(direction));
            ValidateFrames();
            activeDirection = direction;
            SetFrame(activeEvent, activeDirection, activeFrameIndex);
        }

        public void SetRootPresentationEnabled(bool enabled)
        {
            rootPresentationEnabled = enabled;
        }

        public void StartPresentation(CombatUnitPresentationEvent presentationEvent, Vector3 approvedWorldPosition)
        {
            ValidateFrames();
            if (!isPresenting) CaptureRestPose();
            activeEvent = presentationEvent;
            approvedPosition = approvedWorldPosition;
            elapsed = 0f;
            activeFrameIndex = 0;
            castEffectRaised = false;
            isPresenting = presentationEvent != CombatUnitPresentationEvent.Idle;
            SetFrame(activeEvent, activeDirection, activeFrameIndex);
            if (!isPresenting) RestoreRoot();
        }

        public void Tick(float deltaTime)
        {
            if (!isPresenting) return;

            elapsed += Mathf.Max(0f, deltaTime);
            int frame = Mathf.Min(FramesPerDirection - 1, Mathf.FloorToInt(elapsed / FrameDuration));
            if (frame != activeFrameIndex) SetFrame(activeEvent, activeDirection, frame);

            if (activeEvent == CombatUnitPresentationEvent.Cast &&
                !castEffectRaised && activeFrameIndex >= EventFrameIndex)
            {
                castEffectRaised = true;
                CastEffectRequested?.Invoke();
            }

            float progress = Mathf.Clamp01(elapsed / (FrameDuration * FramesPerDirection));
            if (rootPresentationEnabled) ApplyRootPresentation(progress);
            if (progress >= 1f) RestoreRoot();
        }

        public static int EventFrameIndexFor(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Idle:
                case CombatUnitPresentationEvent.Move:
                case CombatUnitPresentationEvent.Attack:
                case CombatUnitPresentationEvent.Hit:
                case CombatUnitPresentationEvent.Cast:
                    return presentationEvent == CombatUnitPresentationEvent.Idle ? 0 : 1;
                case CombatUnitPresentationEvent.Death:
                    return 2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }

        private void ApplyRootPresentation(float progress)
        {
            float arc = Mathf.Sin(progress * Mathf.PI);
            switch (activeEvent)
            {
                case CombatUnitPresentationEvent.Move:
                    transform.position = Vector3.Lerp(restPosition, approvedPosition, progress) + Vector3.up * (0.12f * arc);
                    break;
                case CombatUnitPresentationEvent.Attack:
                    transform.position = restPosition + restRotation * Vector3.forward * (0.1f * arc);
                    transform.rotation = restRotation * Quaternion.Euler(10f * arc, 0f, 0f);
                    break;
                case CombatUnitPresentationEvent.Hit:
                    transform.position = restPosition + restRotation * Vector3.right * (0.05f * Mathf.Sin(progress * Mathf.PI * 4f));
                    break;
                case CombatUnitPresentationEvent.Cast:
                    transform.position = restPosition + Vector3.up * (0.05f * arc);
                    break;
                case CombatUnitPresentationEvent.Death:
                    transform.position = restPosition + Vector3.down * (0.18f * arc);
                    transform.rotation = restRotation * Quaternion.Euler(0f, 0f, 55f * arc);
                    break;
            }
        }

        private void RestoreRoot()
        {
            transform.position = restPosition;
            transform.rotation = restRotation;
            elapsed = 0f;
            isPresenting = false;
            activeEvent = CombatUnitPresentationEvent.Idle;
            SetFrame(activeEvent, activeDirection, 0);
        }

        private void SetFrame(CombatUnitPresentationEvent presentationEvent, int direction, int frame)
        {
            if (direction < 0 || direction >= DirectionCount || frame < 0 || frame >= FramesPerDirection)
                throw new ArgumentOutOfRangeException();
            Sprite[] frames = FramesFor(presentationEvent);
            Sprite sprite = frames[direction * FramesPerDirection + frame];
            if (sprite == null)
                throw new InvalidOperationException("Battle animation sprite is missing a required state, direction, or frame.");
            activeDirection = direction;
            activeFrameIndex = frame;
            if (body != null) body.sprite = sprite;
        }

        private Sprite[] FramesFor(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Idle: return idleFrames;
                case CombatUnitPresentationEvent.Move: return moveFrames;
                case CombatUnitPresentationEvent.Attack: return attackFrames;
                case CombatUnitPresentationEvent.Hit: return hitFrames;
                case CombatUnitPresentationEvent.Cast: return castFrames;
                case CombatUnitPresentationEvent.Death: return deathFrames;
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }

        private void ValidateFrames()
        {
            foreach (CombatUnitPresentationEvent presentationEvent in
                     (CombatUnitPresentationEvent[])Enum.GetValues(typeof(CombatUnitPresentationEvent)))
            {
                Sprite[] frames = FramesFor(presentationEvent);
                if (frames == null || frames.Length != DirectionCount * FramesPerDirection)
                    throw new InvalidOperationException("Battle animation sprite must own all six directions and three frames per state.");
                for (int index = 0; index < frames.Length; index++)
                    if (frames[index] == null)
                        throw new InvalidOperationException("Battle animation sprite is missing a required state, direction, or frame.");
            }
        }
    }

    /// <summary>
    /// 显式切换隔离动态 2D 样例。它只控制 VisualBaselineBoard 上三条技术路线的可见性，
    /// 不触及正式 AdventureUnitSpawner 或任何战斗状态。
    /// </summary>
    public static class BattleAnimationSpriteProbeMatrix
    {
        public const string BoardName = "VisualBaselineBoard";
        public const string GroupName = "BattleAnimationSpriteProbeGroup";
        public const string ProbePrefix = "BattleAnimationSpriteProbe_";
        public const int DirectionCount = 6;

        public static void SetActiveRoute(bool battleAnimation2D)
        {
            GameObject board = GameObject.Find(BoardName);
            if (board == null) return;

            Transform battleGroup = board.transform.Find(GroupName);
            if (battleGroup != null) battleGroup.gameObject.SetActive(battleAnimation2D);

            Transform oldTacticalGroup = board.transform.Find(TacticalSpriteProbeMatrix.GroupName);
            if (oldTacticalGroup != null) oldTacticalGroup.gameObject.SetActive(false);

            for (int direction = 0; direction < DirectionCount; direction++)
            {
                Transform facing = board.transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction);
                if (facing != null) facing.gameObject.SetActive(!battleAnimation2D);
            }
        }
    }
}
