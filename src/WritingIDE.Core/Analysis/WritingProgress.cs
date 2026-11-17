using WritingIDE.Core.Model;

namespace WritingIDE.Core.Analysis;

/// <summary>Where the manuscript stands against its target and deadline.</summary>
/// <param name="Remaining">Words still to write (0 when the target is reached).</param>
/// <param name="DaysLeft">Days until the deadline, counting today; null without a deadline.</param>
/// <param name="WordsPerDayNeeded">Words a day that reach the target by the deadline; null without a deadline or once it has passed.</param>
/// <param name="AveragePerDay">Average words on the days written in the last four weeks.</param>
/// <param name="ProjectedFinish">When the target is reached at the recent pace (writing as often as recently); null when there is no pace to go by.</param>
public sealed record ProgressForecast(int Remaining, int? DaysLeft, int? WordsPerDayNeeded, int AveragePerDay, DateOnly? ProjectedFinish);

/// <summary>The writing log (words written per day), streaks and the forecast.</summary>
public static class WritingProgress {
    private const int RecentDays = 28;

    /// <summary>
    /// Adds a change in the manuscript's length to today's entry. Deleting counts against the day, so a day of cutting
    /// can end below zero.
    /// </summary>
    public static void Record(IList<WritingDay> log, DateOnly today, int delta, int total) {
        int index = log.Count - 1;
        while ((index >= 0) && (log[index].Date > today)) {
            index--;
        }
        if ((index >= 0) && (log[index].Date == today)) {
            log[index] = log[index] with { Written = log[index].Written + delta, Total = total };
        }
        else {
            log.Insert(index + 1, new WritingDay(today, delta, total));
        }
    }

    public static int WrittenOn(IEnumerable<WritingDay> log, DateOnly date) {
        return log.FirstOrDefault(day => day.Date == date)?.Written ?? 0;
    }

    /// <summary>
    /// Days in a row that met the goal (any words at all when there is no goal), ending today. A today that has not
    /// met it yet does not break the streak: it still has until midnight.
    /// </summary>
    public static int Streak(IEnumerable<WritingDay> log, DateOnly today, int dailyGoal) {
        HashSet<DateOnly> met = MetDays(log, dailyGoal);
        DateOnly day = met.Contains(today) ? today : today.AddDays(-1);
        int streak = 0;
        while (met.Contains(day)) {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    public static int BestStreak(IEnumerable<WritingDay> log, int dailyGoal) {
        int best = 0;
        int run = 0;
        DateOnly? previous = null;
        foreach (DateOnly day in MetDays(log, dailyGoal).Order()) {
            run = (previous is DateOnly last) && (last.AddDays(1) == day) ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }
        return best;
    }

    public static ProgressForecast Forecast(IReadOnlyCollection<WritingDay> log, DateOnly today, int total, int target, DateOnly? deadline) {
        int remaining = Math.Max(0, target - total);
        int? daysLeft = deadline is DateOnly date ? date.DayNumber - today.DayNumber + 1 : null;
        int? needed = daysLeft is int days && (days > 0) ? (int)Math.Ceiling((double)remaining / days) : null;

        List<WritingDay> recent = log.Where(day => (day.Date <= today) && (day.Date > today.AddDays(-RecentDays)) && (day.Written > 0)).ToList();
        int average = recent.Count == 0 ? 0 : (int)Math.Round(recent.Average(static day => day.Written));
        DateOnly? finish = null;
        if (remaining == 0) {
            finish = today;
        }
        else if (recent.Count > 0) {
            // The pace over the whole four weeks, days off included.
            double perCalendarDay = (double)recent.Sum(static day => day.Written) / RecentDays;
            double daysToGo = Math.Ceiling(remaining / perCalendarDay);
            finish = daysToGo < 36500 ? today.AddDays((int)daysToGo) : null;
        }
        return new ProgressForecast(remaining, daysLeft, needed, average, finish);
    }

    private static HashSet<DateOnly> MetDays(IEnumerable<WritingDay> log, int dailyGoal) {
        int needed = Math.Max(1, dailyGoal);
        return log.Where(day => day.Written >= needed).Select(static day => day.Date).ToHashSet();
    }
}

/// <summary>Where a beat falls in the manuscript, against where its template expects it.</summary>
/// <param name="StartPercent">Where the beat's chapter starts, as a percentage of the words written; null when unassigned.</param>
/// <param name="EndPercent">Where that chapter ends.</param>
/// <param name="Deviation">How far the expected position lies outside the chapter, in percentage points (0 when inside).</param>
/// <param name="Problem">What to look at, or null.</param>
public sealed record BeatPlacement(Beat Beat, Chapter? Chapter, int? ChapterIndex, double? StartPercent, double? EndPercent, double Deviation, string? Problem);

/// <summary>Compares a beat sheet with where its beats really fall.</summary>
public static class BeatSheetAnalyzer {
    /// <summary>A beat this far (in percentage points) from where the template expects it is reported.</summary>
    public const double Tolerance = 7.5;

    public static IReadOnlyList<BeatPlacement> Analyze(BeatSheet sheet, IReadOnlyList<ChapterText> manuscript) {
        List<int> words = manuscript.Select(static chapter => TextStatistics.CountWords(chapter.Text)).ToList();
        double total = Math.Max(1, words.Sum());
        List<BeatPlacement> placements = [];
        int latestIndex = -1;
        string? latestBeat = null;
        foreach (Beat beat in sheet.Beats) {
            int index = beat.ChapterId is Guid id ? IndexOf(manuscript, id) : -1;
            if (index < 0) {
                placements.Add(new BeatPlacement(beat, null, null, null, null, 0, "Not placed in a chapter yet."));
                continue;
            }
            double start = 100.0 * words.Take(index).Sum() / total;
            double end = 100.0 * words.Take(index + 1).Sum() / total;
            double deviation = beat.ExpectedPercent < start ? start - beat.ExpectedPercent : (beat.ExpectedPercent > end ? beat.ExpectedPercent - end : 0);
            string? problem = null;
            if (index < latestIndex) {
                problem = $"Comes before '{latestBeat}', which the template puts earlier.";
            }
            else if (deviation > Tolerance) {
                problem = beat.ExpectedPercent < start
                    ? $"Late: expected around {beat.ExpectedPercent:0}%, but its chapter starts at {start:0}%."
                    : $"Early: expected around {beat.ExpectedPercent:0}%, but its chapter ends at {end:0}%.";
            }
            if (index >= latestIndex) {
                latestIndex = index;
                latestBeat = beat.Name;
            }
            placements.Add(new BeatPlacement(beat, manuscript[index].Chapter, index, start, end, deviation, problem));
        }
        return placements;
    }

    private static int IndexOf(IReadOnlyList<ChapterText> manuscript, Guid chapterId) {
        for (int i = 0; i < manuscript.Count; i++) {
            if (manuscript[i].Chapter.Id == chapterId) {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>What a plot thread does in one chapter.</summary>
public readonly record struct BraidCell(bool Setup, bool Development, bool Payoff) {
    public bool IsEmpty => !(Setup || Development || Payoff);
}

/// <summary>One plot thread across the chapters.</summary>
/// <param name="Cells">One per chapter, in reading order.</param>
/// <param name="LongestGap">The most chapters in a row, between the thread's first and last passage, in which it does not appear.</param>
/// <param name="GapStart">The chapter index where that gap starts; -1 when there is none.</param>
public sealed record BraidRow(PlotThread Thread, IReadOnlyList<BraidCell> Cells, int LongestGap, int GapStart);

/// <summary>The braid chart: plot threads against chapters, to see a thread going quiet.</summary>
public static class ThreadBraid {
    public static IReadOnlyList<BraidRow> Build(NovelProject project, IReadOnlyList<ChapterText> manuscript) {
        List<BraidRow> rows = [];
        foreach (ThreadReport report in PlotThreadAnalyzer.Analyze(project, manuscript)) {
            BraidCell[] cells = new BraidCell[manuscript.Count];
            foreach (ThreadMarkerLocation marker in report.Markers) {
                BraidCell cell = cells[marker.ChapterIndex];
                cells[marker.ChapterIndex] = marker.Marker.ThreadRole switch {
                    ThreadRole.Setup => cell with { Setup = true },
                    ThreadRole.Payoff => cell with { Payoff = true },
                    _ => cell with { Development = true }
                };
            }
            int longest = 0;
            int gapStart = -1;
            int previous = -1;
            for (int i = 0; i < cells.Length; i++) {
                if (cells[i].IsEmpty) {
                    continue;
                }
                if ((previous >= 0) && (i - previous - 1 > longest)) {
                    longest = i - previous - 1;
                    gapStart = previous + 1;
                }
                previous = i;
            }
            rows.Add(new BraidRow(report.Thread, cells, longest, gapStart));
        }
        return rows;
    }
}