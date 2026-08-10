using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Core;
using TianZhang.Spatial;
using TianZhang.Entity;

using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public interface ICombatCommandHandler
    {
        void RequestBasicAttack();
        void RequestGuard();
        void RequestWait();
        void RequestSwapSpell();
        void RequestSpell(int index);
        void RequestSkill(int index);
    }

    /// <summary>
    /// Formats combat presentation messages without depending on Unity UI objects.
    /// </summary>
    public sealed class CombatLogAdapter
    {
        private readonly Action<string> addLog;
        private readonly Action<string> setStatus;

        public CombatLogAdapter(Action<string> addLog, Action<string> setStatus)
        {
            this.addLog = addLog;
            this.setStatus = setStatus;
        }

        public void AnnounceBattleStart(string playerName, string enemyName)
        {
            addLog?.Invoke($"=== 战斗开始！{playerName} VS {enemyName} ===");
            setStatus?.Invoke($"⚔ {enemyName}");
        }

        public void AppendActionResult(CombatResolver.ActionResult result)
        {
            if (!string.IsNullOrEmpty(result.Message))
                addLog?.Invoke(result.Message);
        }

        public void AppendDropItems(IReadOnlyList<string> dropItems)
        {
            if (dropItems == null || dropItems.Count == 0) return;
            addLog?.Invoke($"掉落: {string.Join(", ", dropItems)}");
        }
    }

    public enum TacticalCombatTeam
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// 唯一遭遇输入。双方顺序由部署生产者提交，不从名称、HP 或格位推导。
    /// </summary>
    public sealed class TacticalCombatSetup
    {
        public TacticalCombatSetup(
            IReadOnlyList<Character> playerMembers,
            IReadOnlyList<Character> enemyMembers,
            SpatialQueryBoard spatialBoard,
            IReadOnlyDictionary<int, HexCoord> unitAnchors,
            IReadOnlyList<AttackProfileData> attackProfiles)
        {
            PlayerMembers = playerMembers;
            EnemyMembers = enemyMembers;
            SpatialBoard = spatialBoard;
            UnitAnchors = unitAnchors;
            AttackProfiles = attackProfiles;
        }

        public IReadOnlyList<Character> PlayerMembers { get; }
        public IReadOnlyList<Character> EnemyMembers { get; }
        public SpatialQueryBoard SpatialBoard { get; }
        public IReadOnlyDictionary<int, HexCoord> UnitAnchors { get; }
        public IReadOnlyList<AttackProfileData> AttackProfiles { get; }
    }

    public readonly struct TacticalCombatMember
    {
        public TacticalCombatMember(
            Character character,
            TacticalCombatTeam team,
            int inputOrder,
            AttackProfileData basicAttackProfile)
        {
            Character = character;
            Team = team;
            InputOrder = inputOrder;
            BasicAttackProfile = basicAttackProfile;
        }

        public Character Character { get; }
        public TacticalCombatTeam Team { get; }
        public int InputOrder { get; }
        public AttackProfileData BasicAttackProfile { get; }
    }

    /// <summary>
    /// 单场战斗唯一的成员、阵营与存活资格所有者；不保存第二份格位或空间查询。
    /// </summary>
    public sealed class TacticalCombatSession
    {
        private readonly List<TacticalCombatMember> members;

        internal TacticalCombatSession(
            IEnumerable<TacticalCombatMember> members,
            SpatialQueryBoard spatialBoard,
            IReadOnlyDictionary<int, HexCoord> unitAnchors)
        {
            this.members = members.ToList();
            SpatialBoard = spatialBoard;
            UnitAnchors = unitAnchors;
        }

        public SpatialQueryBoard SpatialBoard { get; }
        public IReadOnlyDictionary<int, HexCoord> UnitAnchors { get; }
        public IReadOnlyList<TacticalCombatMember> Members => members;

        public List<CTBEngine.CTBUnit> CreateActiveUnitList()
        {
            var result = new List<CTBEngine.CTBUnit>(members.Count);
            foreach (var member in members.OrderBy(entry => entry.InputOrder))
            {
                if (member.Character?.CTBUnit == null)
                    continue;
                member.Character.CTBUnit.IsAlive = member.Character.IsAlive;
                if (member.Character.IsAlive)
                    result.Add(member.Character.CTBUnit);
            }
            return result;
        }

        public bool TryGetMember(int unitId, out TacticalCombatMember member)
        {
            foreach (var candidate in members)
            {
                if (candidate.Character?.CTBUnit != null && candidate.Character.CTBUnit.Id == unitId)
                {
                    member = candidate;
                    return true;
                }
            }

            member = default;
            return false;
        }

        public bool TryGetEligibleSingleTarget(
            int actorUnitId,
            int requestedTargetUnitId,
            out TacticalCombatMember actor,
            out TacticalCombatMember target,
            out string reason)
        {
            if (!TryGetMember(actorUnitId, out actor) || !actor.Character.IsAlive)
            {
                target = default;
                reason = "combat_session_actor_invalid";
                return false;
            }
            if (!TryGetMember(requestedTargetUnitId, out target) || !target.Character.IsAlive || target.Team == actor.Team)
            {
                reason = "combat_session_target_invalid";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<CombatResolver.AreaTargetCandidate> GetAreaCandidates(int actorUnitId)
        {
            if (!TryGetMember(actorUnitId, out var actor))
                return Array.Empty<CombatResolver.AreaTargetCandidate>();

            var candidates = new List<CombatResolver.AreaTargetCandidate>(members.Count);
            foreach (var candidate in members.OrderBy(entry => entry.InputOrder))
            {
                AttackAreaTargetFaction faction = candidate.Character.CTBUnit.Id == actorUnitId
                    ? AttackAreaTargetFaction.Self
                    : candidate.Team == actor.Team
                        ? AttackAreaTargetFaction.Ally
                        : AttackAreaTargetFaction.Enemy;
                candidates.Add(new CombatResolver.AreaTargetCandidate(
                    candidate.Character.CTBUnit.Id,
                    candidate.Character.Position,
                    candidate.Character.IsAlive,
                    faction));
            }
            return candidates;
        }

        public bool HasLivingMembers(TacticalCombatTeam team)
        {
            return members.Any(member => member.Team == team && member.Character.IsAlive);
        }

        public IEnumerable<TacticalCombatMember> GetDefeatedMembers(TacticalCombatTeam team)
        {
            return members.Where(member => member.Team == team && !member.Character.IsAlive);
        }
    }

    public struct TacticalActionAdvance
    {
        public Character Actor { get; }
        public CTBEngine.CTBUnit Unit { get; }
        public int TicksElapsed { get; }

        public TacticalActionAdvance(Character actor, CTBEngine.CTBUnit unit, int ticksElapsed)
        {
            Actor = actor;
            Unit = unit;
            TicksElapsed = ticksElapsed;
        }
    }

    public enum TacticalCombatEndOutcome
    {
        Ongoing,
        Victory,
        Defeat,
    }

    public struct TacticalCombatEndResult
    {
        public TacticalCombatEndOutcome Outcome { get; }
        public string Message { get; }
        public IReadOnlyList<int> DefeatedEnemyUnitIds { get; }

        public bool IsEnded => Outcome != TacticalCombatEndOutcome.Ongoing;

        public TacticalCombatEndResult(
            TacticalCombatEndOutcome outcome,
            string message,
            IReadOnlyList<int> defeatedEnemyUnitIds = null)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
            DefeatedEnemyUnitIds = defeatedEnemyUnitIds ?? Array.Empty<int>();
        }
    }

    /// <summary>
    /// CTB 战斗调度边界：只持有当前会话、引擎、解析器和 AI。
    /// </summary>
    public sealed class TacticalCombatController
    {
        private TacticalCombatSession currentSession;

        public CTBEngine Engine { get; }
        public CombatResolver Resolver { get; }
        public IAIController AIController { get; }

        public TacticalCombatController()
            : this(new CTBEngine(), null, null)
        {
        }

        public TacticalCombatController(CTBEngine engine, CombatResolver resolver = null, IAIController aiController = null)
        {
            Engine = engine ?? new CTBEngine();
            Resolver = resolver ?? new CombatResolver();
            Resolver.Engine = Engine;
            AIController = aiController ?? new SimpleAI();
        }

        public TacticalCombatSession CurrentSession => currentSession;

        public bool TryBeginCombat(
            TacticalCombatSetup setup,
            HexGrid grid,
            out TacticalCombatSession session,
            out string reason)
        {
            session = null;
            if (grid == null || setup?.SpatialBoard == null || setup.UnitAnchors == null || setup.AttackProfiles == null)
            {
                reason = "combat_session_setup_invalid";
                return false;
            }
            if (!TryCreateMembers(setup, grid, out var members, out reason))
                return false;

            // 所有失败前置均已完成，以下才改变 CT、朝向和会话。
            foreach (var member in members)
            {
                Character opponent = members.First(candidate => candidate.Team != member.Team).Character;
                member.Character.FaceTarget(opponent.Position);
                Engine.ResetUnitCT(member.Character.CTBUnit);
                InitializeGongFaStacks(member.Character);
            }
            Engine.ClearActionQueue();
            Resolver.Grid = grid;
            Resolver.SpatialBoard = setup.SpatialBoard;
            session = new TacticalCombatSession(members, setup.SpatialBoard, setup.UnitAnchors);
            currentSession = session;
            reason = string.Empty;
            return true;
        }

        public TacticalActionAdvance AdvanceUntilAction()
        {
            EnsureSession();
            var (unit, ticksElapsed) = Engine.AdvanceUntilAction(currentSession.CreateActiveUnitList());
            return new TacticalActionAdvance(unit?.UserData as Character, unit, ticksElapsed);
        }

        public void AdvanceCooldowns(int ticks)
        {
            EnsureSession();
            foreach (var member in currentSession.Members)
                Resolver.AdvanceCooldowns(member.Character, ticks);
        }

        public CombatResolver.ActionResult ExecuteBasicAttack(int actorUnitId, int targetUnitId)
        {
            if (!TryResolveSingleTarget(actorUnitId, targetUnitId, out var actor, out var target, out var failure))
                return failure;
            if (actor.BasicAttackProfile == null)
                return Failure("basic_attack_profile_unresolved");

            var result = Resolver.BasicAttack(actor.Character, target.Character, actor.BasicAttackProfile);
            ConsumeActionIfSuccessful(actor.Character, result);
            return result;
        }

        public CombatResolver.ActionResult ExecuteArt(
            int actorUnitId,
            int targetUnitId,
            int slotIndex,
            AttackProfileData[] profiles)
        {
            if (!TryResolveSingleTarget(actorUnitId, targetUnitId, out var actor, out var target, out var failure))
                return failure;
            if (!TryResolveAbilityProfile(actor.Character, profiles, slotIndex, AttackProfileKind.Art, out var profile))
                return Failure("attack_profile_unresolved");

            var result = Resolver.CastSpell(actor.Character, target.Character, slotIndex, profile);
            ConsumeActionIfSuccessful(actor.Character, result);
            return result;
        }

        public CombatResolver.ActionResult ExecuteDivine(
            int actorUnitId,
            int targetUnitId,
            int slotIndex,
            AttackProfileData[] profiles)
        {
            if (!TryResolveSingleTarget(actorUnitId, targetUnitId, out var actor, out var target, out var failure))
                return failure;
            if (!TryResolveAbilityProfile(actor.Character, profiles, slotIndex, AttackProfileKind.Divine, out var profile))
                return Failure("attack_profile_unresolved");

            var result = Resolver.UseSkill(actor.Character, target.Character, slotIndex, profile);
            ConsumeActionIfSuccessful(actor.Character, result);
            return result;
        }

        public CombatResolver.AreaTargetingResult ResolveAreaTargets(
            int actorUnitId,
            AttackProfileData profile,
            HexCoord? targetCell)
        {
            EnsureSession();
            if (!currentSession.TryGetMember(actorUnitId, out var actor) || !actor.Character.IsAlive)
                return new CombatResolver.AreaTargetingResult(null, null, "combat_session_actor_invalid");
            return Resolver.ResolveAreaTargets(
                profile,
                actor.Character,
                targetCell,
                currentSession.GetAreaCandidates(actorUnitId));
        }

        public CombatResolver.ActionResult ExecuteGuard(int actorUnitId)
        {
            if (!TryGetLivingActor(actorUnitId, out var actor))
                return Failure("combat_session_actor_invalid");
            var result = Resolver.Guard(actor.Character);
            ConsumeActionIfSuccessful(actor.Character, result);
            return result;
        }

        public CombatResolver.ActionResult ExecuteWait(int actorUnitId)
        {
            if (!TryGetLivingActor(actorUnitId, out var actor))
                return Failure("combat_session_actor_invalid");
            return Resolver.Wait(actor.Character);
        }

        public CombatResolver.ActionResult ExecuteSwapSpell(int actorUnitId, int slotIndex, string newProfileId)
        {
            if (!TryGetLivingActor(actorUnitId, out var actor))
                return Failure("combat_session_actor_invalid");
            if (actor.Character.CombatSwapsUsed >= Character.MaxCombatSwaps)
                return Failure("本场战斗换法次数已用完");

            string oldProfileId = actor.Character.SwapSpellInCombat(slotIndex, newProfileId);
            if (oldProfileId == null)
                return Failure("换法失败");

            ConsumeAction(actor.Character);
            return new CombatResolver.ActionResult
            {
                Success = true,
                Message = $"临阵换法: {oldProfileId} → {newProfileId} (CD×2, 剩余{Character.MaxCombatSwaps - actor.Character.CombatSwapsUsed}次)",
            };
        }

        public TacticalCombatEndResult ResolveBattleEnd(HexGrid grid)
        {
            EnsureSession();
            if (!currentSession.HasLivingMembers(TacticalCombatTeam.Player))
            {
                foreach (var member in currentSession.Members.Where(member => member.Team == TacticalCombatTeam.Player))
                    member.Character.CTBUnit.IsAlive = member.Character.IsAlive;
                return new TacticalCombatEndResult(TacticalCombatEndOutcome.Defeat, "玩家被击败！游戏结束");
            }
            if (currentSession.HasLivingMembers(TacticalCombatTeam.Enemy))
                return new TacticalCombatEndResult(TacticalCombatEndOutcome.Ongoing, string.Empty);

            var defeated = currentSession.GetDefeatedMembers(TacticalCombatTeam.Enemy).ToList();
            foreach (var member in defeated)
            {
                member.Character.CTBUnit.IsAlive = false;
                grid?.ClearOccupied(member.Character.Position);
            }
            string message = defeated.Count == 1
                ? $"击败了 {defeated[0].Character.Name}！"
                : "击败了敌方队伍！";
            return new TacticalCombatEndResult(
                TacticalCombatEndOutcome.Victory,
                message,
                defeated.Select(member => member.Character.CTBUnit.Id).ToArray());
        }

        public string ExecuteEnemyTurn(
            int actorUnitId,
            int targetUnitId,
            AttackProfileData[] arts,
            AttackProfileData[] divines,
            IAIController aiController,
            HexGrid grid)
        {
            if (!TryResolveSingleTarget(actorUnitId, targetUnitId, out var actor, out var target, out var failure))
                return failure.Message;
            if (aiController == null)
                return EnemyAIProfileResolver.UnknownProfileReason;
            Resolver.Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            return aiController.ExecuteTurn(
                actor.Character,
                target.Character,
                arts,
                divines,
                actor.BasicAttackProfile,
                Resolver);
        }

        public void ConsumeAction(Character character)
        {
            if (character?.CTBUnit != null)
                Engine.ConsumeAction(character.CTBUnit);
        }

        private bool TryCreateMembers(
            TacticalCombatSetup setup,
            HexGrid grid,
            out List<TacticalCombatMember> members,
            out string reason)
        {
            members = null;
            if (!HasValidSideCardinality(setup.PlayerMembers, setup.EnemyMembers))
            {
                reason = "combat_session_side_cardinality_invalid";
                return false;
            }

            var profileById = new Dictionary<string, AttackProfileData>(StringComparer.Ordinal);
            foreach (var profile in setup.AttackProfiles)
            {
                if (profile == null || !profile.TryValidate(out _) ||
                    profileById.ContainsKey(profile.attackProfileId))
                {
                    reason = "combat_session_attack_profile_invalid";
                    return false;
                }
                profileById.Add(profile.attackProfileId, profile);
            }

            var seenCharacters = new HashSet<Character>();
            var seenUnitIds = new HashSet<int>();
            members = new List<TacticalCombatMember>(setup.PlayerMembers.Count + setup.EnemyMembers.Count);
            if (!TryAppendMembers(
                    setup.PlayerMembers,
                    TacticalCombatTeam.Player,
                    0,
                    setup.UnitAnchors,
                    grid,
                    profileById,
                    seenCharacters,
                    seenUnitIds,
                    members,
                    out reason) ||
                !TryAppendMembers(
                    setup.EnemyMembers,
                    TacticalCombatTeam.Enemy,
                    setup.PlayerMembers.Count,
                    setup.UnitAnchors,
                    grid,
                    profileById,
                    seenCharacters,
                    seenUnitIds,
                    members,
                    out reason))
            {
                members = null;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool TryAppendMembers(
            IReadOnlyList<Character> source,
            TacticalCombatTeam team,
            int startOrder,
            IReadOnlyDictionary<int, HexCoord> unitAnchors,
            HexGrid grid,
            IReadOnlyDictionary<string, AttackProfileData> profileById,
            ISet<Character> seenCharacters,
            ISet<int> seenUnitIds,
            ICollection<TacticalCombatMember> members,
            out string reason)
        {
            for (int index = 0; index < source.Count; index++)
            {
                Character character = source[index];
                if (character == null || character.CTBUnit == null ||
                    !seenCharacters.Add(character) || !seenUnitIds.Add(character.CTBUnit.Id))
                {
                    reason = "combat_session_participant_invalid";
                    return false;
                }
                if (!unitAnchors.TryGetValue(character.CTBUnit.Id, out var anchor))
                {
                    reason = "combat_session_unit_anchor_missing";
                    return false;
                }
                if (anchor.Q != character.Position.q || anchor.R != character.Position.r ||
                    grid.GetOccupant(character.Position) != character.CTBUnit.Id)
                {
                    reason = "combat_session_unit_anchor_mismatch";
                    return false;
                }
                if (!TryResolveBasicAttackProfile(character, profileById, out var basicProfile, out reason))
                    return false;

                character.CTBUnit.UserData = character;
                character.CTBUnit.IsAlive = character.IsAlive;
                members.Add(new TacticalCombatMember(character, team, startOrder + index, basicProfile));
            }

            reason = string.Empty;
            return true;
        }

        private static bool TryResolveBasicAttackProfile(
            Character character,
            IReadOnlyDictionary<string, AttackProfileData> profiles,
            out AttackProfileData profile,
            out string reason)
        {
            profile = null;
            bool hasMain = !string.IsNullOrWhiteSpace(character.MainEquipmentBasicAttackProfileId);
            bool hasUnarmed = !string.IsNullOrWhiteSpace(character.UnarmedBasicAttackProfileId);
            if (hasMain == hasUnarmed)
            {
                reason = "basic_attack_binding_missing_or_ambiguous";
                return false;
            }

            string expectedId = hasMain
                ? character.MainEquipmentBasicAttackProfileId
                : character.UnarmedBasicAttackProfileId;
            if (!profiles.TryGetValue(expectedId, out profile))
            {
                reason = "basic_attack_profile_not_found";
                return false;
            }

            bool validBinding = profile.profileKind == AttackProfileKind.Basic &&
                (hasMain && profile.basicBindingKind == BasicAttackBindingKind.MainEquipment ||
                 hasUnarmed && profile.basicBindingKind == BasicAttackBindingKind.UnarmedFallback);
            if (!validBinding || character.BasicAttackProfileId != expectedId ||
                character.BasicAttackBindingKind != (hasMain ? "main_equipment" : "unarmed_fallback"))
            {
                profile = null;
                reason = "basic_attack_profile_binding_kind_invalid";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool HasValidSideCardinality(
            IReadOnlyList<Character> playerMembers,
            IReadOnlyList<Character> enemyMembers)
        {
            return playerMembers != null && enemyMembers != null &&
                playerMembers.Count is >= 1 and <= 2 &&
                enemyMembers.Count == playerMembers.Count;
        }

        private bool TryResolveSingleTarget(
            int actorUnitId,
            int targetUnitId,
            out TacticalCombatMember actor,
            out TacticalCombatMember target,
            out CombatResolver.ActionResult failure)
        {
            EnsureSession();
            if (currentSession.TryGetEligibleSingleTarget(actorUnitId, targetUnitId, out actor, out target, out string reason))
            {
                failure = default;
                return true;
            }

            failure = Failure(reason);
            return false;
        }

        private bool TryGetLivingActor(int unitId, out TacticalCombatMember actor)
        {
            EnsureSession();
            return currentSession.TryGetMember(unitId, out actor) && actor.Character.IsAlive;
        }

        private static bool TryResolveAbilityProfile(
            Character character,
            AttackProfileData[] profiles,
            int slotIndex,
            AttackProfileKind expectedKind,
            out AttackProfileData profile)
        {
            profile = null;
            if (profiles == null || slotIndex < 0 || slotIndex >= profiles.Length || profiles[slotIndex] == null)
                return false;
            string[] equippedIds = expectedKind == AttackProfileKind.Art
                ? character.EquippedSpellIds
                : character.EquippedSkillIds;
            if (equippedIds == null || slotIndex >= equippedIds.Length ||
                !string.Equals(equippedIds[slotIndex], profiles[slotIndex].attackProfileId, StringComparison.Ordinal) ||
                profiles[slotIndex].profileKind != expectedKind)
            {
                return false;
            }

            profile = profiles[slotIndex];
            return true;
        }

        private static void InitializeGongFaStacks(Character character)
        {
            if (character.GongFaName == "抱元守一经")
                character.ShouyiStacks = 2;
            if (character.GongFaName == "云篆度人经")
                character.FudanStacks = 2;
            if (character.GongFaName == "九霄雷劫录")
                character.LeijieStacks = 0;
        }

        private void ConsumeActionIfSuccessful(Character character, CombatResolver.ActionResult result)
        {
            if (result.Success)
                ConsumeAction(character);
        }

        private static CombatResolver.ActionResult Failure(string message) =>
            new CombatResolver.ActionResult { Success = false, Message = message };

        private void EnsureSession()
        {
            if (currentSession == null)
                throw new InvalidOperationException("A validated TacticalCombatSetup must be accepted before tactical combat advances.");
        }
    }
}
