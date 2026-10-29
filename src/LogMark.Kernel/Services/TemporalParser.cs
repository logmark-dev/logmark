using System.Text.RegularExpressions;
using LogMark.Models;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;

namespace LogMark.Services;

/// <summary>
/// Parses temporal expressions using Microsoft.Recognizers.Text.DateTime
/// Handles: today, tomorrow, in 3 days, next friday, jan 15, 2pm, etc.
/// </summary>
public partial class TemporalParser
{
    private readonly TimeOnly _eodTime;

    // Abbreviated day names that Microsoft.Recognizers may not handle
    private static readonly Dictionary<string, DayOfWeek> AbbreviatedDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday
    };

    // Matches augmented format: "original (YYYY-MM-DD)" or "original (YYYY-MM-DD, HH:mm)"
    [GeneratedRegex(@"\((\d{4}-\d{2}-\d{2}(?:,\s*\d{2}:\d{2})?)\)", RegexOptions.Compiled)]
    private static partial Regex AugmentedDatePattern();

    public TemporalParser(TimeOnly? eodTime = null)
    {
        _eodTime = eodTime ?? new TimeOnly(17, 0);
    }

    /// <summary>
    /// True when a recognized span carries no time or date marker at all - only
    /// digits, optionally decimal, optionally behind the preposition that made
    /// the recognizer look. See the call site for why this is rejected.
    ///
    /// The preposition has to be part of the pattern because it is part of the
    /// SPAN: measured, the recognizer returns "at 0.25" rather than "0.25", and
    /// returns nothing at all for "0.25" on its own. The preposition is what
    /// turns a number into a time here, so a check that only looked at the
    /// digits would never fire on the real input.
    /// </summary>
    private static bool IsBareNumber(string? span) =>
        span is not null && BareNumberPattern().IsMatch(span.Trim());

    [GeneratedRegex(@"^(?:at|by|on)?\s*\d+(?:[.,]\d+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BareNumberPattern();

    /// <param name="requireFullSpan">
    /// When true, a Microsoft.Recognizers match is only trusted if it spans the whole
    /// input (no word characters left over). This stops a temporal fragment buried in
    /// prose - e.g. "eod" inside "EOD The manual LinkedIn page publish" - from being
    /// treated as the deadline. Used by NotationParser Strategy 1, whose preposition
    /// capture can run across sentence noise. The EOD/EOW/weekday special-cases below
    /// already require a full-string match, so they are unaffected. See the 2026-06-08
    /// deadline-misparse trace and DESK112.
    /// </param>
    public Deadline? ParseDeadline(string text, bool requireFullSpan = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var original = text.Trim();
        var normalized = original.ToLowerInvariant();

        // Check for augmented format: "original (YYYY-MM-DD HH:mm)"
        // This format preserves original temporal text with resolved datetime in parentheses
        var augmentedMatch = AugmentedDatePattern().Match(text);
        if (augmentedMatch.Success)
        {
            var isoDate = augmentedMatch.Groups[1].Value;
            if (DateTime.TryParse(isoDate.Replace(",", ""), out var parsedDate))
            {
                // Ensure time component (00:00 if not present)
                return new Deadline
                {
                    DateTime = parsedDate,
                    OriginalText = augmentedMatch.Value
                };
            }
        }

        // Handle EOD/COB specially (not in Microsoft.Recognizers)
        if (normalized == "eod" || normalized == "cob")
        {
            return new Deadline
            {
                DateTime = DateTime.Today.Add(_eodTime.ToTimeSpan()),
                OriginalText = original
            };
        }

        // Handle EOW (End of Week) - next Friday at 00:00
        if (normalized == "eow")
        {
            return new Deadline
            {
                DateTime = GetNextDayOfWeek(DayOfWeek.Friday), // Already returns date at 00:00
                OriginalText = original
            };
        }

        // Try abbreviated day names first (Microsoft.Recognizers may not handle these)
        if (AbbreviatedDays.TryGetValue(normalized, out var targetDay))
        {
            return new Deadline
            {
                DateTime = GetNextDayOfWeek(targetDay), // Returns date at 00:00
                OriginalText = original
            };
        }

        // Use Microsoft.Recognizers.Text for everything else
        var results = DateTimeRecognizer.RecognizeDateTime(text, Culture.English);

        if (results.Count == 0)
            return null;

        // A BARE NUMBER IS A QUANTITY, NOT A TIME (lmk-ylg7). The recognizer
        // reads "20" as 20:00, so "leave the cap at 20" acquires a deadline of
        // eight this evening and is overdue tomorrow morning - with nothing in
        // the text a reader can point at to explain why. Measured on real
        // agent-written tasks: "the entry cap at 20", "the coverage floor at
        // 0.25", "the chunk budget at 60000". Settings, thresholds and versions
        // are what technical prose is full of.
        //
        // Every genuine time or date carries a marker the bare number lacks -
        // a colon, am/pm, a month or weekday name, an ordinal suffix, or date
        // separators - so requiring one costs only the 24-hour-without-colon
        // form ("at 20" meaning 8pm), which English speakers rarely write and
        // which is unrecoverable from the fabricated-deadline side.
        //
        // Skips rather than rejects outright: "cap at 20 by friday" has a real
        // deadline behind the decoy, and returning null on the first candidate
        // would lose it.
        var result = results.FirstOrDefault(r => !IsBareNumber(r.Text));
        if (result is null)
            return null;

        // Strategy-1 guard: reject a match that only covers a fragment of the input,
        // leaving prose behind (e.g. "eod" recognized inside a noisy multi-word capture).
        if (requireFullSpan)
        {
            var leftover = text.Replace(result.Text, "", StringComparison.OrdinalIgnoreCase);
            if (Regex.IsMatch(leftover, @"\w"))
                return null;
        }

        var resolutionValues = result.Resolution["values"] as List<Dictionary<string, string>>;

        if (resolutionValues == null || resolutionValues.Count == 0)
            return null;

        var firstValue = resolutionValues[0];

        // Parse datetime - always with time component (00:00 if not specified)
        if (firstValue.TryGetValue("value", out var valueStr))
        {
            if (DateTime.TryParse(valueStr, out var parsedDateTime))
            {
                // For day-of-week inputs, ensure we get the NEXT occurrence (not past)
                if (parsedDateTime.Date < DateTime.Today && IsDayOfWeekRelatedInput(normalized))
                {
                    parsedDateTime = GetNextDayOfWeek(parsedDateTime.DayOfWeek);
                }

                return new Deadline
                {
                    DateTime = parsedDateTime, // Already has time (00:00 if date-only)
                    OriginalText = original
                };
            }
        }

        // Handle relative time expressions that return start/end
        if (firstValue.TryGetValue("start", out var startStr))
        {
            if (DateTime.TryParse(startStr, out var startDateTime))
            {
                return new Deadline
                {
                    DateTime = startDateTime,
                    OriginalText = original
                };
            }
        }

        return null;
    }

    private static DateTime GetNextDayOfWeek(DayOfWeek targetDay)
    {
        var today = DateTime.Today;
        var daysUntilTarget = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;

        // If daysUntilTarget is 0, it means today - return next week
        if (daysUntilTarget == 0)
            daysUntilTarget = 7;

        return today.AddDays(daysUntilTarget);
    }

    [GeneratedRegex(@"^this\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ThisDayPattern();

    private static bool IsDayOfWeekRelatedInput(string normalized)
    {
        // Check full day names
        if (Enum.TryParse<DayOfWeek>(normalized, ignoreCase: true, out _))
            return true;

        // Check abbreviated names
        if (AbbreviatedDays.ContainsKey(normalized))
            return true;

        // Check "this friday" patterns
        return ThisDayPattern().IsMatch(normalized);
    }

}
