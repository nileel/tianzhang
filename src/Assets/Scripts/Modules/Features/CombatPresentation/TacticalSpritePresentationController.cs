using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// 2D 六向战术精灵表现控制器。只消费既有 <see cref="StaticChessPresentationEvent"/>；
    /// 六张方向 Sprite 按固定索引保存（缺项、越界即失败关闭）；待机静止，移动、攻击、受击、
    /// 施法和死亡只写同一角色根节点并在结束后复位，施法最多暴露一次既有语义的一次性效果信号。
    /// 子 SpriteRenderer 仅做统一相机平面对齐，不写方向私有偏移、缩放、镜像或旋转补偿。
    /// </summary>
    public sealed class TacticalSpritePresentationController : MonoBehaviour
    {
        private const float SequenceDuration = 0.32f;

        [SerializeField] private Sprite[] directionSprites = new Sprite[6];
        [SerializeField] private int activeDirection;

        private SpriteRenderer body;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 approvedPosition;
        private StaticChessPresentationEvent activeEvent;
        private float elapsed;
        private bool isPresenting;

        public event Action CastEffectRequested;

        public bool IsPresenting => isPresenting;

        public int ActiveDirection => activeDirection;

        public string ActiveSpriteName => body != null && body.sprite != null ? body.sprite.name : null;

        public SpriteRenderer Body => body;

        private void Awake()
        {
            body = GetComponentInChildren<SpriteRenderer>(true);
            if (body == null)
                throw new InvalidOperationException("Tactical sprite prefab must contain exactly one SpriteRenderer.");
            CaptureRestPose();
            ApplyDirection(activeDirection);
        }

        private void Update()
        {
            if (isPresenting) Tick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            AlignBodyToCameraPlane();
        }

        public void CaptureRestPose()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;
            elapsed = 0f;
            isPresenting = false;
        }

        public void SetDirection(int direction)
        {
            if (direction < 0 || direction >= 6)
                throw new ArgumentOutOfRangeException(nameof(direction));
            if (directionSprites == null || directionSprites.Length != 6)
                throw new InvalidOperationException("Tactical sprite must own exactly six frozen direction sprites.");
            if (directionSprites[direction] == null)
                throw new InvalidOperationException("Tactical sprite is missing the frozen direction sprite: " + direction + ".");
            activeDirection = direction;
            if (body != null) body.sprite = directionSprites[direction];
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

        private void ApplyDirection(int direction)
        {
            if (directionSprites == null || direction < 0 || direction >= directionSprites.Length) return;
            if (body != null) body.sprite = directionSprites[direction];
        }

        private void AlignBodyToCameraPlane()
        {
            if (body == null) return;
            Camera camera = Camera.main;
            if (camera == null) return;
            body.transform.rotation = camera.transform.rotation;
        }
    }

    /// <summary>
    /// 2D／3D 隔离矩阵的显式路线切换。2D 战术精灵组与六个 3D 静态棋子探针占用同一组格位，
    /// 必须互斥：true 只启用 2D 并关闭 3D，false 反之。切换后非目标路线不参与渲染。
    /// </summary>
    public static class TacticalSpriteProbeMatrix
    {
        public const string BoardName = "VisualBaselineBoard";
        public const string GroupName = "TacticalSpriteProbeGroup";
        public const string FacingProbePrefix = "FacingProbe_";
        public const int DirectionCount = 6;

        public static void SetActiveRoute(bool tactical2D)
        {
            GameObject board = GameObject.Find(BoardName);
            if (board == null) return;

            Transform group = board.transform.Find(GroupName);
            if (group != null) group.gameObject.SetActive(tactical2D);

            for (int direction = 0; direction < DirectionCount; direction++)
            {
                Transform facing = board.transform.Find(FacingProbePrefix + direction);
                if (facing != null) facing.gameObject.SetActive(!tactical2D);
            }
        }
    }
}
