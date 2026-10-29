using Microsoft.Data.Sqlite;
using LogMark.Models;

namespace LogMark.Services;

public record FeedActionSignal(string Detector, string Action, DateTime Timestamp);
public record AgentWriteBaseline(string Anchor, string Producer, string ContentHash, string Content, string? Route, DateTime Timestamp);
public record AgentWriteOutcomeRecord(string Anchor, string Producer, string Outcome, string? Magnitude, DateTime Timestamp);

/// <summary>
/// Query layer for event log
/// All queries are read-only
/// </summary>
public class EventQueryService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public EventQueryService(string dbPath)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        OpenConnection();
    }

    private void OpenConnection()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
    }

    public int GetEventCount()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<EventQueryResult> GetEventsByType(EventType eventType)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM events WHERE event_type = @type ORDER BY timestamp DESC";
        cmd.Parameters.AddWithValue("@type", eventType.ToString());

        var results = new List<EventQueryResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEventRecord(reader));
        }

        return results;
    }

    private EventQueryResult ReadEventRecord(SqliteDataReader reader)
    {
        return new EventQueryResult
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("timestamp"))),
            SessionId = reader.GetString(reader.GetOrdinal("session_id")),
            EventType = Enum.Parse<EventType>(reader.GetString(reader.GetOrdinal("event_type"))),
            Data = new EventData
            {
                EntryId = GetNullableString(reader, "entry_id"),
                EntryType = GetNullableString(reader, "entry_type"),
                Route = GetNullableString(reader, "route"),
                TagsJson = GetNullableString(reader, "tags_json"),
                ProjectsJson = GetNullableString(reader, "projects_json"),
                HasPaste = reader.GetInt32(reader.GetOrdinal("has_paste")) == 1,
                PasteType = GetNullableString(reader, "paste_type"),
                HasDeadline = reader.GetInt32(reader.GetOrdinal("has_deadline")) == 1,
                ContentLength = reader.GetInt32(reader.GetOrdinal("content_length")),
                CommandName = GetNullableString(reader, "command_name"),
                ModalName = GetNullableString(reader, "modal_name"),
                DataJson = GetNullableString(reader, "data_json")
            }
        };
    }

    private string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Get entry count grouped by hour of day (0-23)
    /// </summary>
    public Dictionary<int, int> GetEntriesByHourOfDay(DateTime startDate, DateTime endDate)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                CAST(strftime('%H', timestamp) AS INTEGER) as hour,
                COUNT(*) as count
            FROM events
            WHERE event_type = 'EntryCreated'
              AND timestamp >= @start
              AND timestamp <= @end
            GROUP BY hour
            ORDER BY hour
        ";

        cmd.Parameters.AddWithValue("@start", startDate.ToString("O"));
        cmd.Parameters.AddWithValue("@end", endDate.ToString("O"));

        var results = new Dictionary<int, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var hour = reader.GetInt32(0);
            var count = reader.GetInt32(1);
            results[hour] = count;
        }

        return results;
    }

    /// <summary>
    /// Get session durations (time between first and last event per session)
    /// </summary>
    public Dictionary<string, TimeSpan> GetSessionDurations()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                session_id,
                MIN(timestamp) as first_event,
                MAX(timestamp) as last_event
            FROM events
            GROUP BY session_id
        ";

        var results = new Dictionary<string, TimeSpan>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var sessionId = reader.GetString(0);
            var firstEvent = DateTime.Parse(reader.GetString(1));
            var lastEvent = DateTime.Parse(reader.GetString(2));
            results[sessionId] = lastEvent - firstEvent;
        }

        return results;
    }

    /// <summary>
    /// Get entry counts grouped by route
    /// </summary>
    public Dictionary<string, int> GetEntryCountsByRoute(DateTime startDate, DateTime endDate)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                COALESCE(route, '(unrouted)') as route,
                COUNT(*) as count
            FROM events
            WHERE event_type = 'EntryCreated'
              AND timestamp >= @start
              AND timestamp <= @end
            GROUP BY route
            ORDER BY count DESC
        ";

        cmd.Parameters.AddWithValue("@start", startDate.ToString("O"));
        cmd.Parameters.AddWithValue("@end", endDate.ToString("O"));

        var results = new Dictionary<string, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var route = reader.GetString(0);
            var count = reader.GetInt32(1);
            results[route] = count;
        }

        return results;
    }

    /// <summary>
    /// Get paste vs type entry counts
    /// Returns (pasteCount, typeCount)
    /// </summary>
    public (int pasteCount, int typeCount) GetPasteVsTypeRatio(DateTime startDate, DateTime endDate)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                SUM(CASE WHEN has_paste = 1 THEN 1 ELSE 0 END) as paste_count,
                SUM(CASE WHEN has_paste = 0 THEN 1 ELSE 0 END) as type_count
            FROM events
            WHERE event_type = 'EntryCreated'
              AND timestamp >= @start
              AND timestamp <= @end
        ";

        cmd.Parameters.AddWithValue("@start", startDate.ToString("O"));
        cmd.Parameters.AddWithValue("@end", endDate.ToString("O"));

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var pasteCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetInt64(0));
            var typeCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetInt64(1));
            return (pasteCount, typeCount);
        }

        return (0, 0);
    }

    /// <summary>
    /// Get command usage distribution
    /// </summary>
    public Dictionary<string, int> GetCommandUsageDistribution()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                command_name,
                COUNT(*) as count
            FROM events
            WHERE event_type = 'CommandExecuted'
              AND command_name IS NOT NULL
            GROUP BY command_name
            ORDER BY count DESC
        ";

        var results = new Dictionary<string, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var commandName = reader.GetString(0);
            var count = reader.GetInt32(1);
            results[commandName] = count;
        }

        return results;
    }

    /// <summary>
    /// Get modal usage distribution
    /// </summary>
    public Dictionary<string, int> GetModalUsageDistribution()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                modal_name,
                COUNT(*) as count
            FROM events
            WHERE event_type = 'ModalOpened'
              AND modal_name IS NOT NULL
            GROUP BY modal_name
            ORDER BY count DESC
        ";

        var results = new Dictionary<string, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var modalName = reader.GetString(0);
            var count = reader.GetInt32(1);
            results[modalName] = count;
        }

        return results;
    }

    /// <summary>
    /// Get navigation handoff distribution (by navigator type and action)
    /// </summary>
    public Dictionary<string, int> GetNavigationHandoffDistribution()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                data_json,
                COUNT(*) as count
            FROM events
            WHERE event_type IN ('SearchHandoff', 'FileNavigationHandoff')
              AND data_json IS NOT NULL
            GROUP BY data_json
            ORDER BY count DESC
        ";

        var results = new Dictionary<string, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var dataJson = reader.GetString(0);
            var count = reader.GetInt32(1);

            // Parse JSON to get meaningful label
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
                if (data != null)
                {
                    string label;
                    if (data.ContainsKey("handoff_to"))
                    {
                        // SearchHandoff event
                        label = $"search -> {data["handoff_to"]}";
                    }
                    else if (data.ContainsKey("navigator") && data.ContainsKey("action"))
                    {
                        // FileNavigationHandoff event
                        var navigator = data["navigator"].ToString();
                        var action = data["action"].ToString();
                        label = $"{action} ({navigator})";
                    }
                    else
                    {
                        label = "unknown";
                    }

                    if (results.ContainsKey(label))
                        results[label] += count;
                    else
                        results[label] = count;
                }
            }
            catch
            {
                // Skip unparseable JSON
            }
        }

        return results;
    }

    /// <summary>
    /// Get entry count grouped by day of week (0=Sunday, 6=Saturday)
    /// </summary>
    public Dictionary<int, int> GetEntriesByDayOfWeek(DateTime startDate, DateTime endDate)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                CAST(strftime('%w', timestamp) AS INTEGER) as day_of_week,
                COUNT(*) as count
            FROM events
            WHERE event_type = 'EntryCreated'
              AND timestamp >= @start
              AND timestamp <= @end
            GROUP BY day_of_week
            ORDER BY day_of_week
        ";

        cmd.Parameters.AddWithValue("@start", startDate.ToString("O"));
        cmd.Parameters.AddWithValue("@end", endDate.ToString("O"));

        var results = new Dictionary<int, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var dayOfWeek = reader.GetInt32(0);
            var count = reader.GetInt32(1);
            results[dayOfWeek] = count;
        }

        return results;
    }

    /// <summary>
    /// Get task postpone patterns for procrastination analysis
    /// </summary>
    public Dictionary<string, PostponePattern> GetPostponePatterns()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT
                entry_id,
                data_json,
                timestamp
            FROM events
            WHERE event_type = 'TaskPostponed'
              AND entry_id IS NOT NULL
            ORDER BY entry_id, timestamp
        ";

        var patterns = new Dictionary<string, PostponePattern>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var entryId = reader.GetString(0);
            var dataJson = reader.GetString(1);
            var timestamp = DateTime.Parse(reader.GetString(2));

            // Parse postpone data
            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
            var postponeCount = data != null && data.ContainsKey("postpone_count")
                ? Convert.ToInt32(data["postpone_count"].ToString())
                : 1;
            var daysAdded = data != null && data.ContainsKey("days_added")
                ? Convert.ToInt32(data["days_added"].ToString())
                : 0;

            if (!patterns.ContainsKey(entryId))
            {
                patterns[entryId] = new PostponePattern
                {
                    TaskId = entryId,
                    PostponeCount = postponeCount,
                    TotalDaysAdded = daysAdded,
                    LastPostpone = timestamp
                };
            }
            else
            {
                // Update with latest postpone
                patterns[entryId].PostponeCount = postponeCount;
                patterns[entryId].TotalDaysAdded += daysAdded;
                patterns[entryId].LastPostpone = timestamp;
            }
        }

        return patterns;
    }

    /// <summary>
    /// Get routes with no activity for specified days
    /// </summary>
    public List<string> GetDeadRoutes(int inactiveDays)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT route
            FROM events
            WHERE route IS NOT NULL
              AND route NOT IN (
                  SELECT DISTINCT route
                  FROM events
                  WHERE event_type = 'EntryCreated'
                    AND route IS NOT NULL
                    AND timestamp >= @cutoff
              )
        ";

        var cutoff = DateTime.Now.AddDays(-inactiveDays);
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));

        var deadRoutes = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            deadRoutes.Add(reader.GetString(0));
        }

        return deadRoutes;
    }

    /// <summary>
    /// Return up to <paramref name="limit"/> most-recent focus session records
    /// reconstructed from ProtocolCompleted event rows. Rows whose data_json is
    /// missing, malformed, or does not name protocol="focus" are skipped.
    /// </summary>
    public IReadOnlyList<LogMark.Services.Protocols.FocusSessionRecord> GetRecentFocusSessions(int limit)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT data_json
            FROM events
            WHERE event_type = 'ProtocolCompleted'
            ORDER BY timestamp DESC";
        using var reader = cmd.ExecuteReader();

        var results = new List<LogMark.Services.Protocols.FocusSessionRecord>();
        while (reader.Read() && results.Count < limit)
        {
            if (reader.IsDBNull(0)) continue;
            var json = reader.GetString(0);
            var record = TryParseFocusSession(json);
            if (record != null) results.Add(record);
        }

        return results;
    }

    private static LogMark.Services.Protocols.FocusSessionRecord? TryParseFocusSession(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("protocol", out var protocol)) return null;
            if (protocol.GetString() != "focus") return null;

            var duration = root.GetProperty("duration_minutes").GetInt32();
            var actual   = root.GetProperty("actual_minutes").GetDouble();
            var postpone = root.GetProperty("postpone_count").GetInt32();
            var outcome  = root.GetProperty("outcome").GetString() ?? "";

            return new LogMark.Services.Protocols.FocusSessionRecord(duration, postpone, outcome, actual);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Feed reaction signals (FeedCardActioned + EntryDiscarded) at or after
    /// <paramref name="since"/>, newest first. EntryDiscarded is normalized to the
    /// "dismissed" action so distill discards tune the distill detector's cadence
    /// the same way feed dismisses tune other detectors. Detector is read from the
    /// data_json.detector field; rows with missing/unparseable payload are skipped.
    /// </summary>
    public List<FeedActionSignal> GetFeedActionSignalsSince(DateTime since)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT event_type, data_json, timestamp
            FROM events
            WHERE event_type IN ('FeedCardActioned', 'EntryDiscarded')
              AND timestamp >= @since
            ORDER BY timestamp DESC";
        cmd.Parameters.AddWithValue("@since", since.ToString("O"));

        var results = new List<FeedActionSignal>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var eventType = reader.GetString(0);
            if (reader.IsDBNull(1)) continue;
            var json = reader.GetString(1);
            var ts = DateTime.Parse(reader.GetString(2));
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("detector", out var det)) continue;
                var detector = det.GetString();
                if (string.IsNullOrEmpty(detector)) continue;
                var action = eventType == "EntryDiscarded"
                    ? "dismissed"
                    : (root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "");
                results.Add(new FeedActionSignal(detector, action, ts));
            }
            catch
            {
                // Skip unparseable payloads.
            }
        }
        return results;
    }

    /// <summary>
    /// Agent-write baselines, newest per anchor. The baseline carries the
    /// content as written so later sweeps can compute edit magnitude.
    /// </summary>
    public List<AgentWriteBaseline> GetAgentWriteBaselines()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT entry_id, route, timestamp, data_json
            FROM events
            WHERE event_type = 'AgentWrite' AND entry_id IS NOT NULL
            ORDER BY timestamp ASC";

        var byAnchor = new Dictionary<string, AgentWriteBaseline>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(3)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(reader.GetString(3));
                var root = doc.RootElement;
                var anchor = reader.GetString(0);
                byAnchor[anchor] = new AgentWriteBaseline(
                    anchor,
                    root.GetProperty("producer").GetString() ?? "",
                    root.GetProperty("contentHash").GetString() ?? "",
                    root.GetProperty("content").GetString() ?? "",
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    DateTime.Parse(reader.GetString(2)));
            }
            catch
            {
                // Skip unparseable payloads.
            }
        }
        return byAnchor.Values.ToList();
    }

    /// <summary>All agent-write outcome signals (kept / edited / deleted).</summary>
    public List<AgentWriteOutcomeRecord> GetAgentWriteOutcomes()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT entry_id, timestamp, data_json
            FROM events
            WHERE event_type = 'AgentWriteOutcome' AND entry_id IS NOT NULL
            ORDER BY timestamp ASC";

        var results = new List<AgentWriteOutcomeRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(2)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(reader.GetString(2));
                var root = doc.RootElement;
                results.Add(new AgentWriteOutcomeRecord(
                    reader.GetString(0),
                    root.GetProperty("producer").GetString() ?? "",
                    root.GetProperty("outcome").GetString() ?? "",
                    root.TryGetProperty("magnitude", out var m) ? m.GetString() : null,
                    DateTime.Parse(reader.GetString(1))));
            }
            catch
            {
                // Skip unparseable payloads.
            }
        }
        return results;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
