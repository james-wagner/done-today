using System;

namespace DoneToday;

public readonly record struct DateRange(DateTime? From, DateTime? To)
{
    public bool Contains(DateTime ts)
    {
        if (From.HasValue && ts < From.Value) return false;
        if (To.HasValue && ts > To.Value) return false;
        return true;
    }

    public static DateRange All => new(null, null);

    public static DateRange ForPreset(string? tag)
    {
        var today = DateTime.Today;
        var endOfToday = today.AddDays(1).AddTicks(-1);
        return tag switch
        {
            "Today"     => new(today, endOfToday),
            "Yesterday" => new(today.AddDays(-1), today.AddTicks(-1)),
            "Last7"     => new(today.AddDays(-6), endOfToday),
            "Last14"    => new(today.AddDays(-13), endOfToday),
            "Last30"    => new(today.AddDays(-29), endOfToday),
            "Last90"    => new(today.AddDays(-89), endOfToday),
            _           => All
        };
    }

    /// <summary>Inclusive of both end-of-day from "to" date and start-of-day "from" date.</summary>
    public static DateRange ForCustom(DateTime? from, DateTime? to)
    {
        DateTime? f = from?.Date;
        DateTime? t = to?.Date.AddDays(1).AddTicks(-1);
        return new(f, t);
    }
}
