using System;
using System.IO;
using UnityEngine;

namespace TianZhang.Infrastructure.Persistence
{
    public static class GameSaveSerializer
    {
        public const int SchemaVersion = 1;

        public static string Serialize(GameSaveEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.schemaVersion != SchemaVersion)
                throw new InvalidDataException("Only save schema 1 can be serialized.");
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
            if (envelope.schemaVersion != SchemaVersion)
                throw new InvalidDataException("Unsupported save schema: " + envelope.schemaVersion + ".");
            return envelope;
        }
    }
}
