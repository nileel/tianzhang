using System;
using System.Collections.Generic;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public enum CombatStatusKind { Stun, Immobilize, SealSpirit, DamageOverTime, Attribute, Xuantong }
    public enum CombatStatusPolarity { BUFF, DEBUFF }
    [Flags]
    public enum CombatStatusTags { ATTRIBUTE = 1, OFFENSE = 2, DEFENSE = 4, ACTION = 8, CONTROL = 16 }
    public enum CombatStatusSourceKind { SELF_ABILITY, OTHER_ABILITY }

    public sealed class CombatOwnTurnStatus
    {
        internal CombatOwnTurnStatus(CombatStatusKind kind, int ends, int damage, bool defer)
        { Kind = kind; RemainingOwnTurnEnds = ends; DamagePerStart = damage; DeferCurrentEnd = defer; }
        public CombatStatusKind Kind { get; }
        public int RemainingOwnTurnEnds { get; internal set; }
        public int DamagePerStart { get; }
        internal bool DeferCurrentEnd { get; set; }
        public string StatusId { get; internal set; }
        public string AttributeId { get; internal set; }
        public long DeltaNumerator { get; internal set; }
        public int RelativeLimitPermille { get; internal set; }
        public int AttributeCapacity { get; internal set; }
        public string SourceRef { get; internal set; }
        public CombatStatusSourceKind SourceKind { get; internal set; }
        public CombatStatusPolarity Polarity { get; internal set; }
        public CombatStatusTags Tags { get; internal set; }
        public string RemovalPolicy => "NORMAL";
        internal CombatOwnTurnStatus Copy() => (CombatOwnTurnStatus)MemberwiseClone();
    }

    /// <summary>Scalar protocol projection; production currently passes an explicit empty source list.
    /// Overlapping sources require a separately approved stacking contract and are rejected by Session.</summary>
    public sealed class CombatActionEconomyModifier
    {
        public CombatActionEconomyModifier(string sourceInstanceId, string sourceDefinitionId,
            CombatCommandKind kind, string profileId, int fixedReductionUnits, int retainedCostPermille,
            int minimumNetCostUnits, int ctPenaltyReduction, int reuseCooldownReductionTicks,
            int refundUnits, int refundLimitPerOwnTurn, int refundBudgetPerOwnTurnUnits)
        {
            if (string.IsNullOrWhiteSpace(sourceInstanceId) || string.IsNullOrWhiteSpace(sourceDefinitionId) ||
                kind is CombatCommandKind.Move or CombatCommandKind.Wait or CombatCommandKind.EndTurn ||
                !Enum.IsDefined(typeof(CombatCommandKind), kind) || fixedReductionUnits < 0 ||
                retainedCostPermille < 0 || retainedCostPermille > 1000 || minimumNetCostUnits <= 0 ||
                ctPenaltyReduction < 0 || reuseCooldownReductionTicks < 0 || refundUnits < 0 ||
                refundLimitPerOwnTurn < 0 || refundBudgetPerOwnTurnUnits < 0 ||
                (refundUnits > 0 && (refundLimitPerOwnTurn == 0 || refundBudgetPerOwnTurnUnits == 0)))
                throw new ArgumentException("Invalid action economy source.");
            SourceInstanceId = sourceInstanceId; SourceDefinitionId = sourceDefinitionId;
            Kind = kind; ProfileId = profileId ?? string.Empty;
            FixedReductionUnits = fixedReductionUnits; RetainedCostPermille = retainedCostPermille;
            MinimumNetCostUnits = minimumNetCostUnits; CtPenaltyReduction = ctPenaltyReduction;
            ReuseCooldownReductionTicks = reuseCooldownReductionTicks; RefundUnits = refundUnits;
            RefundLimitPerOwnTurn = refundLimitPerOwnTurn; RefundBudgetPerOwnTurnUnits = refundBudgetPerOwnTurnUnits;
        }
        public string SourceInstanceId { get; }
        public string SourceDefinitionId { get; }
        public CombatCommandKind Kind { get; }
        public string ProfileId { get; }
        public int FixedReductionUnits { get; }
        public int RetainedCostPermille { get; }
        public int MinimumNetCostUnits { get; }
        public int CtPenaltyReduction { get; }
        public int ReuseCooldownReductionTicks { get; }
        public int RefundUnits { get; }
        public int RefundLimitPerOwnTurn { get; }
        public int RefundBudgetPerOwnTurnUnits { get; }
        internal bool Matches(CombatCommandKind kind, string profile) => Kind == kind &&
            (ProfileId.Length == 0 || string.Equals(ProfileId, profile, StringComparison.Ordinal));
    }

    public enum CombatTeam
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// Pure battle input and mutable battle state for one stable combatant identity.
    /// This type deliberately projects values instead of retaining a Character, scene, or presentation object.
    /// </summary>
    public sealed class CombatantSnapshot
    {
        private Dictionary<string, int> cooldowns = new Dictionary<string, int>(StringComparer.Ordinal);
        private List<string> equippedArtProfileIds;
        private readonly List<string> availableArtProfileIds;
        private readonly List<string> equippedDivineProfileIds;
        private readonly List<string> availableDivineProfileIds;

        public CombatantSnapshot(
            string id,
            CombatTeam team,
            HexCoord position,
            int speed,
            int maximumHealth,
            int currentHealth,
            int physicalAttack,
            int soulAttack,
            int physicalDefense,
            int soulDefense,
            int actionPointMaximumUnits,
            int movementEfficiencyMilliUnits,
            float realmMultiplier = 1f,
            IEnumerable<string> equippedArtProfileIds = null,
            IEnumerable<string> availableArtProfileIds = null,
            int maxCombatSwaps = 2,
            int combatSwapsUsed = 0,
            string basicAttackProfileId = null,
            IEnumerable<string> equippedDivineProfileIds = null,
            IEnumerable<string> availableDivineProfileIds = null,
            int rawSpirit = 0,
            string realmStage = null,
            string visibleRootElement = null,
            string visibleRootGrade = null,
            IEnumerable<CombatActionEconomyModifier> actionEconomyModifiers = null,
            IReadOnlyDictionary<string, string> abilityInstanceIds = null,
            int? rawMind = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Combatant ID is required.", nameof(id));
            if (maximumHealth <= 0 || currentHealth < 0 || currentHealth > maximumHealth)
                throw new ArgumentOutOfRangeException(nameof(currentHealth));
            if (actionPointMaximumUnits <= 0 || movementEfficiencyMilliUnits < 0 || speed <= 0)
                throw new ArgumentOutOfRangeException(nameof(actionPointMaximumUnits));
            if (maxCombatSwaps < 0 || combatSwapsUsed < 0 || combatSwapsUsed > maxCombatSwaps)
                throw new ArgumentOutOfRangeException(nameof(maxCombatSwaps));
            if (rawSpirit < 0)
                throw new ArgumentOutOfRangeException(nameof(rawSpirit));
            if (rawMind < 0) throw new ArgumentOutOfRangeException(nameof(rawMind));

            Id = id;
            Team = team;
            Position = position;
            Speed = speed;
            MaximumHealth = maximumHealth;
            CurrentHealth = currentHealth;
            BasePhysicalAttack = physicalAttack;
            BaseSoulAttack = soulAttack;
            BasePhysicalDefense = physicalDefense;
            BaseSoulDefense = soulDefense;
            RealmMultiplier = Math.Max(1f, realmMultiplier);
            ActionPointMaximumUnits = actionPointMaximumUnits;
            MovementEfficiencyMilliUnits = movementEfficiencyMilliUnits;
            ActionEconomyModifiers = new List<CombatActionEconomyModifier>(actionEconomyModifiers ??
                Array.Empty<CombatActionEconomyModifier>()).AsReadOnly();
            this.abilityInstanceIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in abilityInstanceIds ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException("Shared ability identities must be explicit.", nameof(abilityInstanceIds));
                this.abilityInstanceIds.Add(pair.Key, pair.Value);
            }
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatActionEconomyModifier modifier in ActionEconomyModifiers)
                if (modifier == null || !sources.Add(modifier.SourceInstanceId))
                    throw new ArgumentException("Action economy sources must be unique.", nameof(actionEconomyModifiers));
            this.equippedArtProfileIds = CreateProfileIdList(equippedArtProfileIds);
            this.availableArtProfileIds = CreateProfileIdList(availableArtProfileIds);
            this.equippedDivineProfileIds = CreateProfileIdList(equippedDivineProfileIds);
            this.availableDivineProfileIds = CreateProfileIdList(availableDivineProfileIds);
            BasicAttackProfileId = basicAttackProfileId ?? string.Empty;
            MaxCombatSwaps = maxCombatSwaps;
            CombatSwapsUsed = combatSwapsUsed;
            RawSpirit = rawSpirit;
            RawMind = rawMind.GetValueOrDefault();
            RawMindInputsPresent = rawMind.HasValue;
            RealmStage = realmStage;
            VisibleRootElement = visibleRootElement;
            VisibleRootGrade = visibleRootGrade;
        }

        public string Id { get; }
        public CombatTeam Team { get; }
        public HexCoord Position { get; private set; }
        public int Speed { get; }
        public int MaximumHealth { get; }
        public int CurrentHealth { get; private set; }
        public int MaximumSpirit { get; set; }
        public int CurrentSpirit { get; private set; }
        public int BasePhysicalAttack { get; }
        public int BaseSoulAttack { get; }
        public int BasePhysicalDefense { get; }
        public int BaseSoulDefense { get; }
        public int PhysicalAttack => EffectiveAttribute("physical_attack", BasePhysicalAttack);
        public int SoulAttack => EffectiveAttribute("soul_attack", BaseSoulAttack);
        public int PhysicalDefense => EffectiveAttribute("physical_defense", BasePhysicalDefense);
        public int SoulDefense => EffectiveAttribute("soul_defense", BaseSoulDefense);
        public float RealmMultiplier { get; }
        public int RawSpirit { get; }
        public int RawMind { get; }
        public bool RawMindInputsPresent { get; }
        public string RealmStage { get; }
        public string VisibleRootElement { get; }
        public string VisibleRootGrade { get; }
        public int ActionPointMaximumUnits { get; private set; }
        public int MovementEfficiencyMilliUnits { get; private set; }
        public int ActionPointLimitAtTurnStartUnits { get; private set; }
        public int ActionPointsRemainingUnits { get; private set; }
        public IReadOnlyList<CombatActionEconomyModifier> ActionEconomyModifiers { get; }
        public int HealthRecoveryPerOwnTurn { get; set; }
        public int SpiritRecoveryPerOwnTurn { get; set; }
        public int OwnTurnsStarted { get; private set; }
        public int OwnTurnsEnded { get; private set; }
        public bool HasMovedThisTurn { get; private set; }
        internal bool OwnTurnActive { get; private set; }
        private readonly Dictionary<string, string> abilityInstanceIds;
        private Dictionary<string, (int count, int remaining)> refunds = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        private List<CombatOwnTurnStatus> statuses = new List<CombatOwnTurnStatus>();
        private Dictionary<string, int> committedAbilityUses = new Dictionary<string, int>(StringComparer.Ordinal);
        internal int CommittedBasicAttackUses { get; private set; }
        internal IReadOnlyDictionary<string, int> CommittedAbilityUses => committedAbilityUses;
        public IReadOnlyList<CombatOwnTurnStatus> Statuses => statuses.AsReadOnly();
        public bool HasStatus(CombatStatusKind kind) => statuses.Exists(status => status.Kind == kind);

        public void ApplyStatus(CombatStatusKind kind, int ownTurnEnds, int damagePerStart = 0,
            string sourceRef = null, bool selfAbility = false)
        {
            if (!Enum.IsDefined(typeof(CombatStatusKind), kind) || kind == CombatStatusKind.Attribute ||
                ownTurnEnds <= 0 || damagePerStart < 0 ||
                (kind == CombatStatusKind.Xuantong && (string.IsNullOrWhiteSpace(sourceRef) || damagePerStart != 0)))
                throw new ArgumentOutOfRangeException(nameof(ownTurnEnds));
            if (!IsAlive || kind == CombatStatusKind.Stun &&
                (HasStatus(CombatStatusKind.Stun) || HasStatus(CombatStatusKind.Xuantong))) return;
            if (kind == CombatStatusKind.Xuantong && HasStatus(kind)) return;
            statuses.RemoveAll(status => status.Kind == kind);
            statuses.Add(new CombatOwnTurnStatus(kind, ownTurnEnds, damagePerStart, OwnTurnActive)
            {
                StatusId = kind == CombatStatusKind.Xuantong ? "xuantong" : kind.ToString(),
                SourceRef = sourceRef,
                SourceKind = selfAbility ? CombatStatusSourceKind.SELF_ABILITY : CombatStatusSourceKind.OTHER_ABILITY,
                Polarity = kind == CombatStatusKind.Xuantong ? CombatStatusPolarity.BUFF : CombatStatusPolarity.DEBUFF,
                Tags = CombatStatusTags.ACTION | CombatStatusTags.CONTROL,
            });
        }

        internal void ClearStatuses() => statuses.Clear();

        // Closed mapping of explicit stage inputs; realm multipliers never determine status potency.
        internal bool TryGetRealmOrder(CombatRuleSet rules, out int order)
        {
            string id = RealmStage switch
            {
                "凡人" => "realm_fanren", "练气" => "realm_lianqi",
                "筑基" or "筑基开府" or "开府" or "筑基圆满" => "realm_zhuji",
                "金丹" or "金丹初结" or "金丹温养" or "金丹圆满" => "realm_jindan",
                "元婴" => "realm_yuanying", "化神" => "realm_huashen",
                _ => RealmStage,
            };
            foreach (char grade in "一二三四五六七八九")
                if (RealmStage == "练气" + grade + "品") id = "realm_lianqi";
            foreach (var realm in rules.Realms)
                if (realm.RealmId == id) { order = realm.RealmOrder; return true; }
            order = 0; return false;
        }

        internal void ApplyAttributeStatus(CombatAttackRuleMetadata payload, int coefficient,
            int limit, string sourceRef, bool selfAbility)
        {
            if (!IsAlive) return;
            int basis = payload.StatusAttributeId switch
            {
                "physical_attack" => BasePhysicalAttack, "soul_attack" => BaseSoulAttack,
                "physical_defense" => BasePhysicalDefense, "soul_defense" => BaseSoulDefense,
                _ => throw new ArgumentException("combat_status_attribute_invalid"),
            };
            int capacity = payload.StatusAttributeId switch
            {
                "physical_attack" => payload.Tier.PhysicalAttackCapacity, "soul_attack" => payload.Tier.SoulAttackCapacity,
                "physical_defense" => payload.Tier.PhysicalDefenseCapacity, _ => payload.Tier.SoulDefenseCapacity,
            };
            long relative = Math.Max(-(long)limit * 1000, Math.Min((long)limit * 1000,
                checked((long)payload.StatusRelativePermille * coefficient)));
            long delta = checked(relative * Math.Min(Math.Max(0, basis), capacity));
            CombatOwnTurnStatus previous = statuses.Find(status => status.Kind == CombatStatusKind.Attribute &&
                status.StatusId == payload.StatusId && status.SourceRef == sourceRef);
            if (previous != null && Math.Abs(previous.DeltaNumerator) >= Math.Abs(delta)) return;
            // This slice gives no new overlapping-source permission.
            if (statuses.Exists(status => status.Kind == CombatStatusKind.Attribute &&
                status.AttributeId == payload.StatusAttributeId && status.SourceRef != sourceRef)) return;
            if (previous != null) statuses.Remove(previous);
            statuses.Add(new CombatOwnTurnStatus(CombatStatusKind.Attribute, payload.StatusDurationOwnTurnEnds, 0, OwnTurnActive)
            {
                StatusId = payload.StatusId, AttributeId = payload.StatusAttributeId, DeltaNumerator = delta,
                RelativeLimitPermille = limit, AttributeCapacity = capacity, SourceRef = sourceRef,
                SourceKind = selfAbility ? CombatStatusSourceKind.SELF_ABILITY : CombatStatusSourceKind.OTHER_ABILITY,
                Polarity = relative > 0 ? CombatStatusPolarity.BUFF : CombatStatusPolarity.DEBUFF,
                Tags = CombatStatusTags.ATTRIBUTE | (payload.StatusAttributeId.EndsWith("attack", StringComparison.Ordinal)
                    ? CombatStatusTags.OFFENSE : CombatStatusTags.DEFENSE),
            });
            // Detect overflow on the private preparation copy, before any publication or payment.
            _ = EffectiveAttribute(payload.StatusAttributeId, basis);
        }

        private int EffectiveAttribute(string attribute, int basis)
        {
            long delta = 0;
            int limit = 1000;
            int capacity = int.MaxValue;
            foreach (CombatOwnTurnStatus status in statuses)
                if (status.Kind == CombatStatusKind.Attribute && status.AttributeId == attribute)
                {
                    delta = checked(delta + status.DeltaNumerator);
                    limit = Math.Min(limit, status.RelativeLimitPermille);
                    capacity = Math.Min(capacity, status.AttributeCapacity);
                }
            long peak = checked((long)Math.Min(Math.Max(0, basis), capacity) * limit * 1000);
            delta = Math.Max(-peak, Math.Min(peak, delta));
            long rounded = (Math.Abs(delta) + 500000) / 1000000;
            return checked((int)Math.Max(0, basis + (delta < 0 ? -rounded : rounded)));
        }
        public IReadOnlyList<string> EquippedArtProfileIds => equippedArtProfileIds.AsReadOnly();
        public IReadOnlyList<string> AvailableArtProfileIds => availableArtProfileIds.AsReadOnly();
        public string BasicAttackProfileId { get; set; }
        public IReadOnlyList<string> EquippedDivineProfileIds => equippedDivineProfileIds.AsReadOnly();
        public IReadOnlyList<string> AvailableDivineProfileIds => availableDivineProfileIds.AsReadOnly();
        public int CombatSwapsUsed { get; private set; }
        public int MaxCombatSwaps { get; }
        public float BlockRate { get; set; }
        public float BlockReduction { get; set; }
        public float SoulShieldRate { get; set; }
        public float SoulShieldReduction { get; set; }
        public float DodgeRate { get; set; }
        public float CriticalRate { get; set; }
        public float CriticalDamage { get; set; }
        public float HitRateBonus { get; set; }
        public int Facing { get; set; }
        public bool IsGuarding { get; set; }
        public string GongFaId { get; set; } = string.Empty;
        public string GongFaElement { get; set; } = string.Empty;
        public int ShouyiStacks { get; set; }
        public int FudanStacks { get; set; }
        public int LeijieStacks { get; set; }

        public int MaximumShouyiStacks => CombatGongFaRules.MaximumShouyiStacks(RealmMultiplier);
        public int MaximumFudanStacks => CombatGongFaRules.MaximumFudanStacks(RealmMultiplier);
        public int MaximumLeijieStacks => CombatGongFaRules.MaximumLeijieStacks(RealmMultiplier);
        public float LeijieDamageBonusPerStack => CombatGongFaRules.LeijieDamageBonusPerStack(RealmMultiplier);
        public int MindStrengthBonus => CombatGongFaRules.MindStrengthBonus(GongFaId, RealmMultiplier);

        public bool IsAlive => CurrentHealth > 0;
        public IReadOnlyDictionary<string, int> Cooldowns => cooldowns;

        public void SetPosition(HexCoord position)
        {
            Position = position;
        }

        internal void SetLongTermActionEconomy(int maximum, int movement)
        {
            ActionPointMaximumUnits = maximum;
            MovementEfficiencyMilliUnits = movement;
        }

        internal bool BeginOwnTurn()
        {
            OwnTurnActive = true;
            OwnTurnsStarted = checked(OwnTurnsStarted + 1);
            IsGuarding = false;
            HasMovedThisTurn = false;
            CommittedBasicAttackUses = 0;
            committedAbilityUses.Clear();
            foreach (CombatOwnTurnStatus status in statuses.ToArray())
                if (status.DamagePerStart > 0) ReceiveDamage(status.DamagePerStart);
            if (!IsAlive) return false;
            RestoreHealth(HealthRecoveryPerOwnTurn);
            RestoreSpirit(SpiritRecoveryPerOwnTurn);
            ActionPointLimitAtTurnStartUnits = ActionPointMaximumUnits;
            refunds.Clear();
            foreach (CombatActionEconomyModifier modifier in ActionEconomyModifiers)
                refunds.Add(modifier.SourceInstanceId, (0, modifier.RefundBudgetPerOwnTurnUnits));
            ActionPointsRemainingUnits = HasStatus(CombatStatusKind.Stun) ? 0 : ActionPointLimitAtTurnStartUnits;
            return ActionPointsRemainingUnits > 0;
        }

        internal void EndOwnTurn()
        {
            if (!OwnTurnActive) return;
            OwnTurnActive = false;
            OwnTurnsEnded = checked(OwnTurnsEnded + 1);
            if (IsAlive && GongFaId == CombatGongFaRules.ShouyiGongFaId && !HasMovedThisTurn)
                ShouyiStacks = Math.Min(ShouyiStacks + 1, MaximumShouyiStacks);
            if (IsAlive && GongFaId == CombatGongFaRules.FudanGongFaId)
                FudanStacks = Math.Min(FudanStacks + 1, MaximumFudanStacks);
            foreach (CombatOwnTurnStatus status in statuses)
            {
                if (status.DeferCurrentEnd) status.DeferCurrentEnd = false;
                else status.RemainingOwnTurnEnds--;
            }
            statuses.RemoveAll(status => status.RemainingOwnTurnEnds == 0);
            ClearActionPoints();
        }

        internal void ClearActionPoints()
        {
            ActionPointsRemainingUnits = 0;
            refunds.Clear();
        }

        internal void SpendActionPoints(int cost) => ActionPointsRemainingUnits -= cost;

        internal int RefundActionPoints(CombatActionEconomyModifier modifier, int paid, int minimum)
        {
            if (modifier == null || modifier.RefundUnits == 0) return 0;
            var ledger = refunds[modifier.SourceInstanceId];
            if (ledger.count >= modifier.RefundLimitPerOwnTurn) return 0;
            int refund = QuoteRefund(modifier, paid, minimum, ActionPointsRemainingUnits);
            refunds[modifier.SourceInstanceId] = (ledger.count + 1, ledger.remaining - refund);
            ActionPointsRemainingUnits += refund;
            return refund;
        }

        internal int QuoteRefund(CombatActionEconomyModifier modifier, int paid, int minimum, int remainingAfterCost)
        {
            if (modifier == null || modifier.RefundUnits == 0 || !refunds.TryGetValue(modifier.SourceInstanceId, out var ledger) ||
                ledger.count >= modifier.RefundLimitPerOwnTurn) return 0;
            return Math.Min(modifier.RefundUnits, Math.Min(ledger.remaining,
                Math.Min(paid - minimum, ActionPointLimitAtTurnStartUnits - remainingAfterCost)));
        }

        internal void CommitMovement(HexCoord destination, int cost, int facing)
        {
            if (facing < 0 || facing >= HexCoord.Directions.Length)
                throw new ArgumentOutOfRangeException(nameof(facing));
            HasMovedThisTurn = true;
            Facing = facing;
            Position = destination;
        }

        public void SwapEquippedArt(int slotIndex, string profileId)
        {
            if (slotIndex < 0 || slotIndex >= EquippedArtProfileIds.Count)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID is required.", nameof(profileId));

            equippedArtProfileIds[slotIndex] = profileId;
            CombatSwapsUsed++;
        }

        public void SetSpirit(int maximumSpirit, int currentSpirit)
        {
            if (maximumSpirit < 0 || currentSpirit < 0 || currentSpirit > maximumSpirit)
                throw new ArgumentOutOfRangeException(nameof(currentSpirit));
            MaximumSpirit = maximumSpirit;
            CurrentSpirit = currentSpirit;
        }

        public bool TryConsumeSpirit(int amount)
        {
            if (amount < 0 || CurrentSpirit < amount)
                return false;
            CurrentSpirit -= amount;
            return true;
        }

        public void RestoreSpirit(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            CurrentSpirit += Math.Min(amount, MaximumSpirit - CurrentSpirit);
        }

        public void SetCooldown(string profileId, int ticks)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID is required.", nameof(profileId));
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            cooldowns[GetAbilityInstanceId(profileId)] = ticks;
        }

        public int GetCooldown(string profileId)
        {
            return !string.IsNullOrWhiteSpace(profileId) && cooldowns.TryGetValue(GetAbilityInstanceId(profileId), out int ticks)
                ? ticks
                : 0;
        }

        public string GetAbilityInstanceId(string profileId) =>
            abilityInstanceIds.TryGetValue(profileId, out string instance) ? instance : Id + ":" + profileId;

        internal int GetCommittedUseCount(CombatCommandKind kind, string profileId) =>
            kind == CombatCommandKind.BasicAttack ? CommittedBasicAttackUses :
            committedAbilityUses.TryGetValue(GetAbilityInstanceId(profileId), out int count) ? count : 0;

        internal void RecordCommittedUse(CombatCommandKind kind, string profileId, int count)
        {
            if (kind == CombatCommandKind.BasicAttack) CommittedBasicAttackUses = count;
            else committedAbilityUses[GetAbilityInstanceId(profileId)] = count;
        }

        /// <summary>Private preparation copy; no prepared result is published before the command gate commits.</summary>
        internal CombatantSnapshot CopyForResolution()
        {
            var copy = (CombatantSnapshot)MemberwiseClone();
            copy.cooldowns = new Dictionary<string, int>(cooldowns, StringComparer.Ordinal);
            copy.equippedArtProfileIds = new List<string>(equippedArtProfileIds);
            copy.refunds = new Dictionary<string, (int, int)>(refunds, StringComparer.Ordinal);
            copy.committedAbilityUses = new Dictionary<string, int>(committedAbilityUses, StringComparer.Ordinal);
            copy.statuses = statuses.ConvertAll(status => status.Copy());
            return copy;
        }

        internal void CommitResolvedState(CombatantSnapshot prepared)
        {
            BasicAttackProfileId = prepared.BasicAttackProfileId;
            CurrentHealth = prepared.CurrentHealth; CurrentSpirit = prepared.CurrentSpirit;
            ShouyiStacks = prepared.ShouyiStacks; FudanStacks = prepared.FudanStacks; LeijieStacks = prepared.LeijieStacks;
            IsGuarding = prepared.IsGuarding; HasMovedThisTurn = prepared.HasMovedThisTurn;
            Position = prepared.Position; Facing = prepared.Facing; CombatSwapsUsed = prepared.CombatSwapsUsed;
            cooldowns = prepared.cooldowns; equippedArtProfileIds = prepared.equippedArtProfileIds;
            statuses = prepared.statuses;
            // Payment, refund and successful-use ledgers belong to the command gate, not the effect copy.
            if (!IsAlive) { IsGuarding = false; ClearActionPoints(); ClearStatuses(); }
        }

        public void AdvanceCooldowns(int ticks)
        {
            if (ticks < 0)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            if (ticks == 0)
                return;

            var keys = new List<string>(cooldowns.Keys);
            foreach (string key in keys)
                cooldowns[key] = Math.Max(0, cooldowns[key] - ticks);
        }

        public void ReceiveDamage(int damage)
        {
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));
            if (damage > 0 && GongFaId == CombatGongFaRules.LeijieGongFaId)
                LeijieStacks = Math.Min(LeijieStacks + 1, MaximumLeijieStacks);
            CurrentHealth = Math.Max(0, CurrentHealth - damage);
            if (!IsAlive) { IsGuarding = false; ClearActionPoints(); ClearStatuses(); }
        }

        public float DefenseMultiplier(bool physical)
        {
            return CombatGongFaRules.DefenseMultiplier(this, physical) * (IsGuarding ? 1.5f : 1f);
        }

        public void RestoreHealth(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            CurrentHealth += Math.Min(amount, MaximumHealth - CurrentHealth);
        }

        private static List<string> CreateProfileIdList(IEnumerable<string> profileIds)
        {
            var result = new List<string>();
            if (profileIds == null)
                return result;

            foreach (string profileId in profileIds)
            {
                if (!string.IsNullOrWhiteSpace(profileId) && !result.Contains(profileId))
                    result.Add(profileId);
            }
            return result;
        }
    }

    internal static class CombatGongFaRules
    {
        internal const string ShouyiGongFaId = "抱元守一经";
        internal const string FudanGongFaId = "云篆度人经";
        internal const string LeijieGongFaId = "九霄雷劫录";
        internal const string XuanganGongFaId = "南华玄感录";
        internal const string HanhongGongFaId = "含弘光大典";

        internal static int MaximumShouyiStacks(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 6 ? 5 : realm >= 3 ? 4 : 3;
        }

        internal static int MaximumFudanStacks(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 6 ? 5 : realm >= 3 ? 3 : 5;
        }

        internal static int MaximumLeijieStacks(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 6 ? 5 : 3;
        }

        internal static float LeijieDamageBonusPerStack(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 24 ? 0.30f :
                realm >= 12 ? 0.22f :
                realm >= 6 ? 0.18f :
                0.15f;
        }

        internal static int MindStrengthBonus(string gongFaId, float realmMultiplier)
        {
            if (gongFaId != XuanganGongFaId)
                return 0;

            int realm = RoundedRealm(realmMultiplier);
            return realm >= 12 ? 12 :
                realm >= 6 ? 8 :
                realm >= 3 ? 5 :
                3;
        }

        internal static float DefenseMultiplier(CombatantSnapshot combatant, bool physical)
        {
            if (combatant.GongFaId != HanhongGongFaId)
                return 1f;

            float multiplier = 1f + ZaiwuAllDefenseBonus(combatant);
            if (physical)
                multiplier *= 1f + HanhongPhysicalDefenseBonus(combatant.RealmMultiplier);
            return multiplier;
        }

        private static float HanhongPhysicalDefenseBonus(float realmMultiplier)
        {
            return realmMultiplier >= 24f ? 0.30f :
                realmMultiplier >= 12f ? 0.25f :
                realmMultiplier >= 6f ? 0.20f :
                realmMultiplier >= 3f ? 0.15f :
                realmMultiplier >= 1.5f ? 0.10f :
                0f;
        }

        private static float ZaiwuAllDefenseBonus(CombatantSnapshot combatant)
        {
            if (combatant.MaximumHealth <= 0 || combatant.CurrentHealth >= combatant.MaximumHealth)
                return 0f;

            float cap = combatant.RealmMultiplier >= 24f ? 0.40f :
                combatant.RealmMultiplier >= 6f ? 0.30f :
                combatant.RealmMultiplier >= 3f ? 0.20f :
                0f;
            if (cap <= 0f)
                return 0f;

            float missingHealthRate = (combatant.MaximumHealth - combatant.CurrentHealth) /
                (float)combatant.MaximumHealth;
            float bonus = (float)Math.Floor(Math.Min(1f, Math.Max(0f, missingHealthRate)) * 10f) * 0.02f;
            return Math.Min(bonus, cap);
        }

        private static int RoundedRealm(float realmMultiplier)
        {
            return (int)Math.Round(Math.Max(1f, realmMultiplier), MidpointRounding.ToEven);
        }
    }
}
