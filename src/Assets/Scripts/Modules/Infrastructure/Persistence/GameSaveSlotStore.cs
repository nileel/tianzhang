using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TianZhang.Infrastructure.Persistence
{
    public enum GameSaveSlotFailureReason
    {
        None = 0,
        InvalidSlotId = 1,
        DirectoryUnavailable = 2,
        SlotNotFound = 3,
        ReadFailed = 4,
        InvalidSaveData = 5,
        MissingPlayerPayload = 6,
        WriteFailed = 7,
    }

    public sealed class GameSaveSlotSummary
    {
        public GameSaveSlotSummary(
            string slotId,
            string characterId,
            string characterDisplayName,
            DateTime lastWriteTimeUtc,
            bool isReadable,
            GameSaveSlotFailureReason failureReason)
        {
            SlotId = slotId;
            CharacterId = characterId;
            CharacterDisplayName = characterDisplayName;
            LastWriteTimeUtc = lastWriteTimeUtc;
            IsReadable = isReadable;
            FailureReason = failureReason;
        }

        public string SlotId { get; }
        public string CharacterId { get; }
        public string CharacterDisplayName { get; }
        public DateTime LastWriteTimeUtc { get; }
        public bool IsReadable { get; }
        public GameSaveSlotFailureReason FailureReason { get; }
    }

    public sealed class GameSaveSlotListResult
    {
        private static readonly IReadOnlyList<GameSaveSlotSummary> EmptySlots =
            new List<GameSaveSlotSummary>().AsReadOnly();

        private GameSaveSlotListResult(
            bool succeeded,
            IReadOnlyList<GameSaveSlotSummary> slots,
            GameSaveSlotFailureReason failureReason)
        {
            Succeeded = succeeded;
            Slots = slots ?? EmptySlots;
            FailureReason = failureReason;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<GameSaveSlotSummary> Slots { get; }
        public GameSaveSlotFailureReason FailureReason { get; }

        public static GameSaveSlotListResult Success(IReadOnlyList<GameSaveSlotSummary> slots)
        {
            return new GameSaveSlotListResult(true, slots, GameSaveSlotFailureReason.None);
        }

        public static GameSaveSlotListResult Failed(GameSaveSlotFailureReason failureReason)
        {
            return new GameSaveSlotListResult(false, EmptySlots, failureReason);
        }
    }

    public sealed class GameSaveSlotReadResult
    {
        private GameSaveSlotReadResult(
            GameSaveEnvelope envelope,
            GameSaveSlotFailureReason failureReason)
        {
            Envelope = envelope;
            FailureReason = failureReason;
        }

        public bool Succeeded => Envelope != null && FailureReason == GameSaveSlotFailureReason.None;
        public GameSaveEnvelope Envelope { get; }
        public GameSaveSlotFailureReason FailureReason { get; }

        public static GameSaveSlotReadResult Success(GameSaveEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            return new GameSaveSlotReadResult(envelope, GameSaveSlotFailureReason.None);
        }

        public static GameSaveSlotReadResult Failed(GameSaveSlotFailureReason failureReason)
        {
            return new GameSaveSlotReadResult(null, failureReason);
        }
    }

    public sealed class GameSaveSlotWriteResult
    {
        private GameSaveSlotWriteResult(bool succeeded, GameSaveSlotFailureReason failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
        }

        public bool Succeeded { get; }
        public GameSaveSlotFailureReason FailureReason { get; }

        public static GameSaveSlotWriteResult Success()
        {
            return new GameSaveSlotWriteResult(true, GameSaveSlotFailureReason.None);
        }

        public static GameSaveSlotWriteResult Failed(GameSaveSlotFailureReason failureReason)
        {
            return new GameSaveSlotWriteResult(false, failureReason);
        }
    }

    /// <summary>
    /// Stores schema 1 save envelopes in an explicitly supplied local directory.
    /// It owns file adaptation only and never restores or mutates GameRuntime.
    /// </summary>
    public sealed class GameSaveSlotStore
    {
        private const string SlotExtension = ".json";
        private readonly string directoryPath;

        public GameSaveSlotStore(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("A save slot directory is required.", nameof(directoryPath));

            try
            {
                this.directoryPath = Path.GetFullPath(directoryPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                throw new ArgumentException("The save slot directory is invalid.", nameof(directoryPath), exception);
            }
        }

        public GameSaveSlotListResult ListSlots()
        {
            if (!Directory.Exists(directoryPath))
            {
                if (File.Exists(directoryPath))
                    return GameSaveSlotListResult.Failed(GameSaveSlotFailureReason.DirectoryUnavailable);
                return GameSaveSlotListResult.Success(new List<GameSaveSlotSummary>().AsReadOnly());
            }

            string[] paths;
            try
            {
                paths = Directory.GetFiles(directoryPath, "*" + SlotExtension, SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                return GameSaveSlotListResult.Failed(GameSaveSlotFailureReason.DirectoryUnavailable);
            }

            var summaries = new List<GameSaveSlotSummary>();
            for (int index = 0; index < paths.Length; index++)
            {
                string slotId = Path.GetFileNameWithoutExtension(paths[index]);
                if (!IsValidSlotId(slotId))
                    continue;

                GameSaveSlotReadResult read = ReadFromPath(paths[index]);
                DateTime lastWriteTimeUtc = TryGetLastWriteTimeUtc(paths[index]);
                if (read.Succeeded)
                {
                    summaries.Add(new GameSaveSlotSummary(
                        slotId,
                        read.Envelope.player.characterId,
                        read.Envelope.player.displayName,
                        lastWriteTimeUtc,
                        true,
                        GameSaveSlotFailureReason.None));
                }
                else
                {
                    summaries.Add(new GameSaveSlotSummary(
                        slotId,
                        null,
                        null,
                        lastWriteTimeUtc,
                        false,
                        read.FailureReason));
                }
            }

            summaries.Sort((left, right) => StringComparer.Ordinal.Compare(left.SlotId, right.SlotId));
            return GameSaveSlotListResult.Success(summaries.AsReadOnly());
        }

        public GameSaveSlotReadResult Read(string slotId)
        {
            if (!TryGetSlotPath(slotId, out string slotPath))
                return GameSaveSlotReadResult.Failed(GameSaveSlotFailureReason.InvalidSlotId);
            if (!File.Exists(slotPath))
                return GameSaveSlotReadResult.Failed(GameSaveSlotFailureReason.SlotNotFound);
            return ReadFromPath(slotPath);
        }

        public GameSaveSlotWriteResult Write(string slotId, GameSaveEnvelope envelope)
        {
            if (!TryGetSlotPath(slotId, out string slotPath))
                return GameSaveSlotWriteResult.Failed(GameSaveSlotFailureReason.InvalidSlotId);
            if (envelope == null)
                return GameSaveSlotWriteResult.Failed(GameSaveSlotFailureReason.InvalidSaveData);

            GameSaveSlotFailureReason envelopeFailure = ValidatePlayerPayload(envelope);
            if (envelopeFailure != GameSaveSlotFailureReason.None)
                return GameSaveSlotWriteResult.Failed(envelopeFailure);

            string json;
            try
            {
                json = GameSaveSerializer.Serialize(envelope);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidDataException)
            {
                return GameSaveSlotWriteResult.Failed(GameSaveSlotFailureReason.InvalidSaveData);
            }

            try
            {
                Directory.CreateDirectory(directoryPath);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                return GameSaveSlotWriteResult.Failed(GameSaveSlotFailureReason.DirectoryUnavailable);
            }

            string temporaryPath = Path.Combine(
                directoryPath,
                "." + slotId + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                WriteTemporaryFile(temporaryPath, json);
                if (File.Exists(slotPath))
                    File.Replace(temporaryPath, slotPath, null);
                else
                    File.Move(temporaryPath, slotPath);
                return GameSaveSlotWriteResult.Success();
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                return GameSaveSlotWriteResult.Failed(GameSaveSlotFailureReason.WriteFailed);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }

        private GameSaveSlotReadResult ReadFromPath(string slotPath)
        {
            string json;
            try
            {
                json = File.ReadAllText(slotPath, Encoding.UTF8);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                return GameSaveSlotReadResult.Failed(GameSaveSlotFailureReason.ReadFailed);
            }

            GameSaveEnvelope envelope;
            try
            {
                envelope = GameSaveSerializer.Deserialize(json);
            }
            catch (InvalidDataException)
            {
                return GameSaveSlotReadResult.Failed(GameSaveSlotFailureReason.InvalidSaveData);
            }

            GameSaveSlotFailureReason payloadFailure = ValidatePlayerPayload(envelope);
            return payloadFailure == GameSaveSlotFailureReason.None
                ? GameSaveSlotReadResult.Success(envelope)
                : GameSaveSlotReadResult.Failed(payloadFailure);
        }

        private bool TryGetSlotPath(string slotId, out string slotPath)
        {
            slotPath = null;
            if (!IsValidSlotId(slotId))
                return false;

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(directoryPath, slotId + SlotExtension));
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                return false;
            }

            string rootPrefix = directoryPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            slotPath = candidate;
            return true;
        }

        private static bool IsValidSlotId(string slotId)
        {
            if (string.IsNullOrEmpty(slotId) || slotId.Length > 64)
                return false;

            for (int index = 0; index < slotId.Length; index++)
            {
                char value = slotId[index];
                bool isAsciiLetter = (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
                bool isDigit = value >= '0' && value <= '9';
                if (!isAsciiLetter && !isDigit && value != '_' && value != '-')
                    return false;
            }
            return true;
        }

        private static GameSaveSlotFailureReason ValidatePlayerPayload(GameSaveEnvelope envelope)
        {
            if (!envelope.hasPlayer || envelope.player == null || envelope.cultivation == null)
                return GameSaveSlotFailureReason.MissingPlayerPayload;
            if (string.IsNullOrWhiteSpace(envelope.player.characterId) ||
                string.IsNullOrWhiteSpace(envelope.player.displayName))
            {
                return GameSaveSlotFailureReason.InvalidSaveData;
            }
            return GameSaveSlotFailureReason.None;
        }

        private static void WriteTemporaryFile(string path, string json)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static DateTime TryGetLastWriteTimeUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                return DateTime.MinValue;
            }
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                // Cleanup is best effort; the primary write result remains authoritative.
            }
        }

        private static bool IsFileSystemFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is UnauthorizedAccessException ||
                   exception is NotSupportedException ||
                   exception is PathTooLongException;
        }
    }
}
