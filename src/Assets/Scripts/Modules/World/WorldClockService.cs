using System;

namespace TianZhang.World
{
    /// <summary>Only owner of persistent world calendar state.</summary>
    public sealed class WorldClockService
    {
        public WorldClockService(int year, string seasonId, int day, string timeOfDayId)
        {
            if (year <= 0) throw new ArgumentOutOfRangeException(nameof(year));
            if (day <= 0) throw new ArgumentOutOfRangeException(nameof(day));
            if (string.IsNullOrWhiteSpace(seasonId)) throw new ArgumentException("Season ID is required.", nameof(seasonId));
            if (string.IsNullOrWhiteSpace(timeOfDayId)) throw new ArgumentException("Time-of-day ID is required.", nameof(timeOfDayId));
            Year = year;
            SeasonId = seasonId;
            Day = day;
            TimeOfDayId = timeOfDayId;
        }

        public WorldClockService(int day) : this(387, "autumn", day <= 0 ? 1 : day, "dawn") { }

        public int Year { get; private set; }
        public string SeasonId { get; private set; }
        public int Day { get; private set; }
        public string TimeOfDayId { get; private set; }

        public int AdvanceDay()
        {
            Day = checked(Day + 1);
            return Day;
        }

        public WorldClockSnapshot Capture()
        {
            return new WorldClockSnapshot(Year, SeasonId, Day, TimeOfDayId);
        }

        public void Restore(WorldClockSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var validated = new WorldClockService(
                snapshot.Year,
                snapshot.SeasonId,
                snapshot.Day,
                snapshot.TimeOfDayId);
            Year = validated.Year;
            SeasonId = validated.SeasonId;
            Day = validated.Day;
            TimeOfDayId = validated.TimeOfDayId;
        }
    }

    public sealed class WorldClockSnapshot
    {
        public WorldClockSnapshot(int year, string seasonId, int day, string timeOfDayId)
        {
            Year = year;
            SeasonId = seasonId;
            Day = day;
            TimeOfDayId = timeOfDayId;
        }

        public int Year { get; }
        public string SeasonId { get; }
        public int Day { get; }
        public string TimeOfDayId { get; }
    }
}
