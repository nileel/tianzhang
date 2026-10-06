using System;
using System.IO;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;

namespace TianZhang.Bootstrap
{
    /// <summary>Unique Unity composition root. It only owns creation and lifetime wiring.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private static GameBootstrap instance;
        private GameSaveSlotStore slotStore;
        private string activeSlotId;

        public static GameRuntime Runtime { get; private set; }

        public static GameRuntime RequireRuntime()
        {
            GameBootstrap existing = RequireInstance();
            existing.EnsureRuntime();
            return Runtime;
        }

        public static GameBootstrap RequireInstance()
        {
            if (instance == null) Runtime = null;
            GameBootstrap existing = instance != null
                ? instance
                : UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (existing == null) throw new InvalidOperationException("game_bootstrap_missing");
            if (instance == null) instance = existing;
            existing.EnsureSlotStore();
            return existing;
        }

        public GameSaveSlotStore SlotStore
        {
            get
            {
                EnsureSlotStore();
                return slotStore;
            }
        }

        public void ActivateSlot(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
                throw new ArgumentException("slot_id_required", nameof(slotId));
            activeSlotId = slotId;
        }

        public GameSaveSlotWriteResult SaveActiveSlot()
        {
            if (Runtime == null || Runtime.Player == null || string.IsNullOrWhiteSpace(activeSlotId))
                return GameSaveSlotWriteResult.Failed(GameSaveSlotFailureReason.InvalidSaveData);
            return SlotStore.Write(activeSlotId, Runtime.CaptureSave());
        }

        /// <summary>
        /// Deletes one slot and drops the active-slot reference that pointed at it, so a later
        /// <see cref="SaveActiveSlot"/> cannot recreate the file the player just removed.
        /// </summary>
        public GameSaveSlotDeleteResult DeleteSlot(string slotId)
        {
            GameSaveSlotDeleteResult result = SlotStore.Delete(slotId);
            if (result.Succeeded && string.Equals(activeSlotId, slotId, StringComparison.OrdinalIgnoreCase))
                activeSlotId = null;
            return result;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureRuntime();
            EnsureSlotStore();
        }

        private void EnsureRuntime()
        {
            if (Runtime == null) Runtime = new GameRuntime();
        }

        private void EnsureSlotStore()
        {
            if (slotStore == null)
                slotStore = new GameSaveSlotStore(Path.Combine(Application.persistentDataPath, "SaveSlots"));
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            Runtime = null;
        }
    }
}
