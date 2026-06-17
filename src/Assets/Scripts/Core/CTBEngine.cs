using System;
using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Core
{
    /// <summary>
    /// CTB（Charge-Time Battle）充能制战斗引擎
    /// 时间以"刻"推进，角色 CT ≥ 100 获得行动权
    /// </summary>
    public class CTBEngine
    {
        public const float ActionThreshold = 100f;
        public const float CtRetentionOnWait = 0.5f; // 待机保留50%CT

        public int CurrentTick { get; private set; }
        private List<CTBUnit> units = new List<CTBUnit>();
        private Queue<CTBUnit> actionQueue = new Queue<CTBUnit>();
        private int unitIdCounter;

        // 事件
        public event Action<int> OnTickAdvanced;

        public class CTBUnit
        {
            public int Id;
            public float CT;
            public int Speed;       // 反应（决定CT增长速度）
            public float CtPerTick; // 每刻CT增量 = Speed
            public float NextActionThreshold = ActionThreshold;
            public int PendingCooldownPenalty;
            public bool IsAlive = true;
            public object UserData; // 挂载角色引用
        }

        public CTBUnit RegisterUnit(int speed, object userData = null)
        {
            int safeSpeed = Mathf.Max(1, speed);
            var unit = new CTBUnit
            {
                Id = unitIdCounter++,
                CT = 0,
                Speed = safeSpeed,
                CtPerTick = safeSpeed, // 反应100 = 1刻满CT
                NextActionThreshold = ActionThreshold,
                UserData = userData
            };
            units.Add(unit);
            return unit;
        }

        public void RemoveUnit(CTBUnit unit)
        {
            units.Remove(unit);
        }

        /// <summary>推进1刻，返回获得行动权的单位列表</summary>
        public List<CTBUnit> AdvanceTick(List<CTBUnit> activeUnits = null)
        {
            CurrentTick++;
            OnTickAdvanced?.Invoke(CurrentTick);

            var tickUnits = activeUnits ?? units;
            var readyUnits = new List<CTBUnit>();
            foreach (var unit in tickUnits)
            {
                if (unit == null || !unit.IsAlive) continue;
                unit.CT += unit.CtPerTick;

                if (unit.CT >= unit.NextActionThreshold)
                {
                    readyUnits.Add(unit);
                }
            }
            readyUnits.Sort((a, b) =>
            {
                int speedCompare = b.Speed.CompareTo(a.Speed);
                return speedCompare != 0 ? speedCompare : b.CT.CompareTo(a.CT);
            });
            return readyUnits;
        }

        /// <summary>消耗CT执行行动，并把本次行动产生的冷却惩罚写入下次行动门槛</summary>
        public void ConsumeAction(CTBUnit unit)
        {
            float threshold = Mathf.Max(ActionThreshold, unit.NextActionThreshold);
            unit.CT -= threshold;
            if (unit.CT < 0) unit.CT = 0;
            unit.NextActionThreshold = ActionThreshold + unit.PendingCooldownPenalty;
            unit.PendingCooldownPenalty = 0;
        }

        /// <summary>消耗指定量CT（用于移动拆分等部分行动）</summary>
        public void ConsumePartialCT(CTBUnit unit, float amount)
        {
            unit.CT -= amount;
            if (unit.CT < 0) unit.CT = 0;
        }

        /// <summary>待机：保留50%CT</summary>
        public void WaitAction(CTBUnit unit)
        {
            unit.CT *= CtRetentionOnWait;
            unit.NextActionThreshold = ActionThreshold;
            unit.PendingCooldownPenalty = 0;
        }

        /// <summary>术法冷却惩罚：提高下次行动门槛，而不是直接扣当前CT</summary>
        public void ApplySpellCooldown(CTBUnit unit, int cooldownPenalty)
        {
            unit.PendingCooldownPenalty = Mathf.Max(unit.PendingCooldownPenalty, cooldownPenalty);
        }

        /// <summary>推进直到有单位获得行动权</summary>
        public (CTBUnit unit, int ticksElapsed) AdvanceUntilAction(List<CTBUnit> activeUnits = null)
        {
            int ticks = 0;
            while (true)
            {
                var ready = AdvanceTick(activeUnits);
                ticks++;
                if (ready.Count > 0)
                    return (ready[0], ticks);
                if (ticks > 10000) // 安全阀
                    return (null, ticks);
            }
        }

        /// <summary>重置所有单位CT</summary>
        public void ResetAllCT()
        {
            foreach (var unit in units)
                ResetUnitCT(unit);
        }

        public void ResetUnitCT(CTBUnit unit)
        {
            if (unit == null) return;
            unit.CT = 0;
            unit.NextActionThreshold = ActionThreshold;
            unit.PendingCooldownPenalty = 0;
        }

        public List<CTBUnit> GetAllUnits() => units;

        public CTBUnit GetUnit(int id) => units.Find(u => u.Id == id);
    }
}
