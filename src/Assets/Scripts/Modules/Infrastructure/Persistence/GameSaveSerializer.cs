using System;
using System.IO;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Infrastructure.Persistence
{
    public static class GameSaveSerializer
    {
        public const string EquipmentSaveInvalid = "equipment_save_invalid";
        public const int SchemaVersion = 5;
        public const int LegacyGrowthSchemaVersion = 4;
        public const int LegacySchemaVersion = 1;
        public const int LegacyFoundationPurpleMansionSchemaVersion = 2;
        public const int LegacyActionEconomySchemaVersion = 3;
        public const string LegacyFoundationPurpleMansionSchemaIncompatible =
            FoundationPurpleMansionRuntimeState.LegacyStageSchemaIncompatible;

        /// <summary>Stable rejection reason for a legacy character payload that cannot prove its long-term action economy inputs.</summary>
        public const string LegacyActionEconomyInputMissing = "legacy_action_economy_input_missing";

        /// <summary>Stable rejection reason for a current-schema character payload whose action economy inputs are missing, disguised or invalid.</summary>
        public const string ActionEconomySaveInvalid = "action_economy_save_invalid";

        public static string Serialize(GameSaveEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.schemaVersion != SchemaVersion)
                throw new InvalidDataException("Only save schema " + SchemaVersion + " can be serialized.");
            if (envelope.hasPlayer && envelope.player != null)
                envelope.player.unarmedBasicAttackProfileId = string.Empty;
            string json = JsonUtility.ToJson(envelope);
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Save serialization returned no data.");
            return json;
        }

        public static GameSaveEnvelope Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Save data is empty.");
            GameSaveEnvelope envelope;
            try { envelope = JsonUtility.FromJson<GameSaveEnvelope>(json); }
            catch (Exception exception) { throw new InvalidDataException("Save data is not valid JSON.", exception); }
            if (envelope == null) throw new InvalidDataException("Save data did not contain an envelope.");
            if (envelope.schemaVersion == LegacySchemaVersion ||
                envelope.schemaVersion == LegacyFoundationPurpleMansionSchemaVersion)
            {
                return MigrateLegacySchema(envelope);
            }
            if (envelope.schemaVersion == LegacyActionEconomySchemaVersion)
            {
                return MigrateLegacyActionEconomySchema(envelope);
            }
            if (envelope.schemaVersion == LegacyGrowthSchemaVersion)
            {
                if (envelope.player != null && (envelope.player.movementGrowthInputsPresent ||
                    envelope.player.movementGrowthSources != null && envelope.player.movementGrowthSources.Length != 0))
                    throw new InvalidDataException("legacy_growth_payload_invalid");
                if (envelope.hasPlayer && envelope.player != null)
                {
                    // No old training producer existed: migrate only the explicit empty source set.
                    envelope.player.movementGrowthInputsPresent = true;
                    envelope.player.movementGrowthSources = Array.Empty<MovementGrowthRecord>();
                }
                envelope.schemaVersion = SchemaVersion;
            }
            if (envelope.schemaVersion != SchemaVersion)
                throw new InvalidDataException("Unsupported save schema: " + envelope.schemaVersion + ".");
            if (envelope.hasPlayer && envelope.player != null)
                envelope.player.unarmedBasicAttackProfileId = string.Empty;
            return envelope;
        }

        private static GameSaveEnvelope MigrateLegacySchema(GameSaveEnvelope envelope)
        {
            if (envelope.hasPlayer)
            {
                throw new InvalidDataException(
                    LegacyFoundationPurpleMansionSchemaIncompatible +
                    ": legacy player cultivation data cannot prove the three-stage foundation state.");
            }
            envelope.schemaVersion = SchemaVersion;
            return envelope;
        }

        /// <summary>
        /// Schema 3 stores no explicit long-term action point maximum or movement efficiency input, and neither can be
        /// rebuilt from the stored reaction, gong fa or realm multiplier. A real player payload is therefore rejected
        /// instead of being clamped or back-filled; a truly empty session migrates mechanically and still has to pass
        /// every existing restore validation, which is also where a payload hiding behind hasPlayer=false is caught.
        /// </summary>
        private static GameSaveEnvelope MigrateLegacyActionEconomySchema(GameSaveEnvelope envelope)
        {
            if (envelope.hasPlayer)
            {
                throw new InvalidDataException(
                    LegacyActionEconomyInputMissing +
                    ": legacy player payload has no explicit action point maximum or movement efficiency input.");
            }
            envelope.schemaVersion = SchemaVersion;
            return envelope;
        }
    }
}
