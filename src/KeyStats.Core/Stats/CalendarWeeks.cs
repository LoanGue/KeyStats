using KeyStats.Core.Analysis;

namespace KeyStats.Core.Stats;

/// <summary>Les <c>Count</c> dernières semaines calendaires (lundi à dimanche) jusqu'au jour <c>ToDay</c> compris.</summary>
public readonly record struct CalendarWeeks(int ToDay, int Count)
{
    private DateTime Today => LocalDayClock.DateOf(ToDay);

    /// <summary>Lundi de la première semaine.</summary>
    public DateTime First => Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7) - 7 * (Count - 1));

    /// <summary>Jours couverts, du premier lundi à <c>ToDay</c>.</summary>
    public DayRange Range => new(LocalDayClock.KeyOf(First), ToDay);

    /// <summary>Rang de la semaine qui contient <paramref name="day"/> (aaaammjj), dans <see cref="Range"/>.</summary>
    public int IndexOf(int day) => (int)(LocalDayClock.DateOf(day) - First).TotalDays / 7;

    /// <summary>Lundi de la semaine <paramref name="index"/> (aaaa-mm-jj).</summary>
    public string Monday(int index) => StatsFilter.Iso(LocalDayClock.KeyOf(First.AddDays(7 * index)));

    /// <summary>Vrai pour la dernière semaine tant qu'elle n'est pas finie.</summary>
    public bool Partial(int index) => index == Count - 1 && Today.DayOfWeek != DayOfWeek.Sunday;
}
