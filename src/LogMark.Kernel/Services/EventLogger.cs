using Microsoft.Data.Sqlite;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Logs events to append-only SQLite database
/// </summary>
public class EventLogger : IDisposable
{
    private readonly string _dbPath;
    private readonly SessionTracker _sessionTracker;
    private readonly SystemLogger? _systemLogger;
    private SqliteConnection? _connection;

    public EventLogger(string dbPath, SessionTracker sessionTracker, SystemLogger? systemLogger = null)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));
        _systemLogger = systemLogger;
    }

    public void Initialize()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        CreateSchema();
    }

    private void CreateSchema()
    {
        using var cmd = _connection!.CreateCommand();

        // Create events table
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                session_id TEXT NOT NULL,
                event_type TEXT NOT NULL,

                entry_id TEXT,
                entry_type TEXT,
                route TEXT,
                tags_json TEXT,
                projects_json TEXT,

                has_paste INTEGER NOT NULL DEFAULT 0,
                paste_type TEXT,
                has_deadline INTEGER NOT NULL DEFAULT 0,
                content_length INTEGER,

                command_name TEXT,
                modal_name TEXT,

                data_json TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_events_session ON events(session_id);
            CREATE INDEX IF NOT EXISTS idx_events_type ON events(event_type);
            CREATE INDEX IF NOT EXISTS idx_events_entry ON events(entry_id);
            CREATE INDEX IF NOT EXISTS idx_events_route ON events(route);
            CREATE INDEX IF NOT EXISTS idx_events_entry_type ON events(entry_type);
        ";
        cmd.ExecuteNonQuery();

        // Create schema version table
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY
            );
            INSERT OR IGNORE INTO schema_version (version) VALUES (1);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Log an event to the database
    /// Best-effort: failures are logged but don't throw
    /// </summary>
    public void LogEvent(EventType eventType, EventData data)
    {
        if (_connection == null)
            throw new InvalidOperationException("EventLogger not initialized");

        try
        {
            var eventId = Guid.NewGuid().ToString();
            var timestamp = DateTime.Now.ToString("O"); // ISO 8601
            var sessionId = _sessionTracker.GetCurrentSessionId();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO events (
                    id, timestamp, session_id, event_type,
                    entry_id, entry_type, route, tags_json, projects_json,
                    has_paste, paste_type, has_deadline, content_length,
                    command_name, modal_name, data_json
                ) VALUES (
                    @id, @timestamp, @session_id, @event_type,
                    @entry_id, @entry_type, @route, @tags_json, @projects_json,
                    @has_paste, @paste_type, @has_deadline, @content_length,
                    @command_name, @modal_name, @data_json
                )
            ";

            cmd.Parameters.AddWithValue("@id", eventId);
            cmd.Parameters.AddWithValue("@timestamp", timestamp);
            cmd.Parameters.AddWithValue("@session_id", sessionId);
            cmd.Parameters.AddWithValue("@event_type", eventType.ToString());

            cmd.Parameters.AddWithValue("@entry_id", data.EntryId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@entry_type", data.EntryType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@route", data.Route ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@tags_json", data.TagsJson ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@projects_json", data.ProjectsJson ?? (object)DBNull.Value);

            cmd.Parameters.AddWithValue("@has_paste", data.HasPaste ? 1 : 0);
            cmd.Parameters.AddWithValue("@paste_type", data.PasteType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@has_deadline", data.HasDeadline ? 1 : 0);
            cmd.Parameters.AddWithValue("@content_length", data.ContentLength);

            cmd.Parameters.AddWithValue("@command_name", data.CommandName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@modal_name", data.ModalName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@data_json", data.DataJson ?? (object)DBNull.Value);

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // Log to SystemLogger
            _systemLogger?.Error($"Failed to log event {eventType}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
