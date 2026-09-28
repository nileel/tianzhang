using System;
using System.IO;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Infrastructure.Persistence
{
    public static class GameSaveSerializer
    {
        public const int SchemaVersion = 3;
        public const int LegacySchemaVersion = 1;
        public const int LegacyFoundationPurpleMansionSchemaVersion = 2;
        public const string LegacyFoundationPurpleMansionSchemaIncompatible =
            FoundationPurpleMansionRuntimeState.LegacyStageSchemaIncompatible;

        public static string Serialize(GameSaveEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.schemaVersion != SchemaVersion)
                throw new InvalidDataException("Only save schema 2 can be serialized.");
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
            if (envelope.schemaVersion != SchemaVersion)
                throw new InvalidDataException("Unsupported save schema: " + envelope.schemaVersion + ".");
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
    }
}
