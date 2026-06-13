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
        public event Action<CTBUnit> OnUnitActionReady;
        public event Action<int> OnTickAdvanced;

        public class CTBUnit
        {
            public int Id;
            public float CT;
            public int Speed;       // 反应（决定CT增长速度）
            public float CtPerTick; // 每刻CT增量 = Speed / BaseSpeed
            public bool IsAlive = true;
            public object UserData; // 挂载角色引用
        }

        public CTBUnit RegisterUnit(int speed, object userData = null)
        {
            var unit = new CTBUnit
            {
                Id = unitIdCounter++,
                CT = 0,
                Speed = Mathf.Max(1, speed),
                CtPerTick = speed / 100f, // 反应100 = 1刻满CT
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
        public List<CTBUnit> AdvanceTick()
        {
            CurrentTick++;
            OnTickAdvanced?.Invoke(CurrentTick);

            var readyUnits = new List<CTBUnit>();
            foreach (var unit in units)
            {
                if (!unit.IsAlive) continue;
                unit.CT += unit.CtPerTick;

                if (unit.CT >= ActionThreshold)
                {
                    readyUnits.Add(unit);
                }
            }
            return readyUnits;
        }

        /// <summary>消耗CT执行行动（-100）</summary>
        public void ConsumeAction(CTBUnit unit)
        {
            unit.CT -= ActionThreshold;
        }

        /// <summary>待机：保留50%CT</summary>
        public void WaitAction(CTBUnit unit)
        {
            unit.CT *= CtRetentionOnWait;
        }

        /// <summary>术法冷却推进（在 CT 上叠加冷却惩罚）</summary>
        public void ApplySpellCooldown(CTBUnit unit, int cooldownPenalty)
        {
            unit.CT -= cooldownPenalty;
            if (unit.CT < 0) unit.CT = 0;
        }

        /// <summary>推进直到有单位获得行动权</summary>
        public (CTBUnit unit, int ticksElapsed) AdvanceUntilAction()
        {
            int ticks = 0;
            while (true)
            {
                var ready = AdvanceTick();
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
                unit.CT = 0;
        }

        public List<CTBUnit> GetAllUnits() => units;

        public CTBUnit GetUnit(int id) => units.Find(u => u.Id == id);
    }
}

