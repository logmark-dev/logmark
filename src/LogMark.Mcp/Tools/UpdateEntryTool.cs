using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to update an entry's lifecycle state. Decisions can be advanced to
/// frozen or rejected; blocks to resolved or dismissed. Wraps the desktop
/// DecisionApprovalService (exclusive-tag rewrite, mark-not-delete, idempotent).
/// </summary>
public class UpdateEntryTool : ITool
{
    private readonly VaultServices _services;

    public UpdateEntryTool(VaultServices services) => _services = services;

    public string Name => "update_entry";

    public ToolDefinition Definition => new()
    {
        Name = "update_entry",
        Description = "Advance an entry's lifecycle state by its entry ID. Set status to frozen or rejected on a decision; resolved or dismissed on a block; dismissed on a task that was overtaken rather than done. Set complete: true to mark a task as done. Find this tool when: confirm or reject a decision, mark a blocker resolved, dismiss a block, change an entry's status, mark todo done, close finished action item, drop a task that no longer applies.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["entry_id"] = new()
                {
                    Type = "string",
                    Description = "The entry ID (ShortId) of the entry to update (e.g., '757ef6ae')."
                },
                ["status"] = new()
                {
                    Type = "string",
                    Description = "New lifecycle status. Decision: 'frozen' (confirmed) or 'rejected'. Block: 'resolved' or 'dismissed'. Task: 'dismissed' (requires resolution; use complete: true for a task that was actually finished)."
                },
                ["complete"] = new()
                {
                    Type = "boolean",
                    Description = "Set true to mark a task entry as completed."
                },
                ["resolution"] = new()
                {
                    Type = "string",
                    Description = "Note about how the entry was resolved. Optional with complete (task) and with status: resolved|dismissed (block); REQUIRED with status: dismissed on a task."
                },
                ["deadline"] = new()
                {
                    Type = "string",
                    Description = "Set a task's deadline to an ISO date or date-time ('2026-09-01' or '2026-09-01 14:30'), or pass 'none' to CLEAR a deadline that should never have been set. Overwrites any existing deadline. Can be used on its own, without status or complete."
                },
            },
            Required = new List<string> { "entry_id" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var entryId = McpToolHelpers.GetStringArg(arguments, "entry_id");
        if (string.IsNullOrWhiteSpace(entryId))
            return McpToolHelpers.ErrorResult("Missing required parameter: entry_id");

        var status = McpToolHelpers.GetStringArg(arguments, "status");
        var complete = McpToolHelpers.GetBoolArg(arguments, "complete") ?? false;
        var deadlineArg = McpToolHelpers.GetStringArg(arguments, "deadline");

        if (string.IsNullOrWhiteSpace(status) && !complete && string.IsNullOrWhiteSpace(deadlineArg))
            return McpToolHelpers.ErrorResult(
                "Provide an action: status (decision: frozen|rejected; block: resolved|dismissed), complete: true, or deadline.");

        try
        {
            var (path, entry) = await FindEntryByIdAsync(entryId, ct);
            if (path == null || entry == null)
                return McpToolHelpers.ErrorResult($"Entry not found with ID: {entryId}");
            if (string.IsNullOrEmpty(entry.ShortId))
                return McpToolHelpers.UnaddressableError(entryId, EntryResolver.GetRelativePath(path, _services.VaultPath!));

            var resolution = McpToolHelpers.GetStringArg(arguments, "resolution");

            // Ordered before the lifecycle branches so "clear this bogus deadline"
            // is reachable on its own, which was the whole point of adding it.
            if (!string.IsNullOrWhiteSpace(deadlineArg) && !complete && string.IsNullOrWhiteSpace(status))
            {
                return ChangeDeadline(entryId, path, entry, deadlineArg);
            }

            if (complete)
            {
                return CompleteTask(entryId, path, entry, resolution);
            }

            return await ApplyStatusAsync(entryId, path, entry, status!.Trim().ToLowerInvariant(), resolution);
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"MCP update_entry failed: {ex.Message}");
            return McpToolHelpers.ErrorResult($"Failed to update entry: {ex.Message}");
        }
    }

    /// <summary>
    /// Set or clear a task's deadline (lmk-0hp3). Without this, a deadline that
    /// should never have been set could only be removed by dismissing the task
    /// and recreating it, losing the id and the history - and the recognizer
    /// does invent them from ordinary prose (lmk-ylg7).
    /// </summary>
    private ToolCallResult ChangeDeadline(
        string entryId, string absolutePath, ParsedEntry entry, string deadlineArg)
    {
        if (entry.Type != EntryType.Task)
            return McpToolHelpers.ErrorResult(
                $"deadline is only valid on task entries; this entry is a {entry.Type.ToString().ToLowerInvariant()}.");

        var raw = deadlineArg.Trim();
        DateTime? deadline;
        if (raw.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            deadline = null;
        }
        else if (DateTime.TryParse(raw, out var parsed))
        {
            deadline = parsed;
        }
        else
        {
            return McpToolHelpers.ErrorResult(
                $"Could not read '{deadlineArg}' as a date. Use an ISO date ('2026-09-01'), a date-time ('2026-09-01 14:30'), or 'none' to clear.");
        }

        var relativePath = Path.GetRelativePath(_services.VaultPath!, absolutePath);

        // Report what was verified, not what was attempted. Tasks written by
        // session distill carry an "_id:" line with no deadline field at all, so
        // this genuinely cannot succeed on them and must say so.
        if (!_services.TaskTracker!.ChangeDeadline(relativePath, entryId, deadline))
        {
            _services.Logger!.Warn($"MCP update_entry deadline FAILED: {entryId} in {relativePath}");
            return McpToolHelpers.ErrorResult(
                $"Could not change the deadline on {entryId}: no '_task:' line was rewritten in {relativePath}. Entries written with an '_id:' line carry no deadline field.");
        }

        var routeName = McpToolHelpers.RouteOf(_services, relativePath);
        _services.SessionContext!.RecordModification(relativePath, entryId, routeName);
        _services.Logger!.Info($"MCP update_entry deadline: {entryId} -> {deadline?.ToString("O") ?? "none"}");

        return McpToolHelpers.SuccessResult(new
        {
            Success = true,
            EntryId = entryId,
            EntryType = "task",
            Action = deadline is null ? "deadline_cleared" : "deadline_changed",
            Path = relativePath,
            Deadline = deadline?.ToString("O"),
            ActiveSession = _services.SessionContext!.GetActiveSessionInfo(),
        });
    }

    private ToolCallResult CompleteTask(string entryId, string absolutePath, ParsedEntry entry, string? resolution)
    {
        if (entry.Type != EntryType.Task)
            return McpToolHelpers.ErrorResult($"complete is only valid on task entries; this entry is a {entry.Type.ToString().ToLowerInvariant()}.");

        var relativePath = Path.GetRelativePath(_services.VaultPath!, absolutePath);

        if (entry.IsCompleted)
            return McpToolHelpers.SuccessResult(new
            {
                Success = true,
                EntryId = entryId,
                EntryType = "task",
                Action = "completed",
                Path = relativePath,
                AlreadyCompleted = true,
                Message = "Task was already completed",
                ActiveSession = _services.SessionContext!.GetActiveSessionInfo(),
            });

        var completedAt = DateTime.Now;

        // Report what was verified, not what was attempted. Before 2026-07-27
        // this call's result was discarded and the success payload below was
        // returned unconditionally, so a completion that struck nothing through
        // was indistinguishable from one that worked - 25 such "completions"
        // were logged in a single grooming run without a single task closing.
        if (!_services.TaskTracker!.CompleteTask(relativePath, entryId, completedAt, resolution))
        {
            _services.Logger!.Warn($"MCP update_entry complete FAILED: {entryId} in {relativePath}");
            return McpToolHelpers.ErrorResult(
                $"Could not complete {entryId}: no matching entry was rewritten in {relativePath}.");
        }

        var routeName = McpToolHelpers.RouteOf(_services, relativePath);
        _services.SessionContext!.RecordModification(relativePath, entryId, routeName);

        _services.Logger!.Info($"MCP update_entry complete: {entryId} in {relativePath}");

        return McpToolHelpers.SuccessResult(new
        {
            Success = true,
            EntryId = entryId,
            EntryType = "task",
            Action = "completed",
            Path = relativePath,
            CompletedAt = completedAt.ToString("O"),
            Resolution = resolution,
            AlreadyCompleted = false,
            ActiveSession = _services.SessionContext!.GetActiveSessionInfo(),
        });
    }

    private async Task<ToolCallResult> ApplyStatusAsync(string entryId, string absolutePath, ParsedEntry entry, string status, string? resolution)
    {
        var svc = new DecisionApprovalService(
            _services.EntryCache!, _services.VaultWriter!, new FileAccessor(), _services.Logger);

        bool ok;
        switch (entry.Type)
        {
            case EntryType.Decision:
                if (status == "frozen") ok = await svc.PromoteDecisionAsync(absolutePath, entryId);
                else if (status == "rejected") ok = await svc.RejectDecisionAsync(absolutePath, entryId);
                else return McpToolHelpers.ErrorResult($"Invalid status '{status}' for a decision. Allowed: frozen, rejected.");
                break;
            case EntryType.Block:
                if (status == "resolved") ok = await svc.ResolveBlockAsync(absolutePath, entryId, resolution);
                else if (status == "dismissed") ok = await svc.DismissBlockAsync(absolutePath, entryId, resolution);
                else return McpToolHelpers.ErrorResult($"Invalid status '{status}' for a block. Allowed: resolved, dismissed.");
                break;
            case EntryType.Task:
                // The task's second exit. Completion stays on the "complete"
                // parameter; this closes a task that was overtaken rather than
                // done, without writing the falsehood that the work happened.
                if (status != "dismissed")
                    return McpToolHelpers.ErrorResult(
                        $"Invalid status '{status}' for a task. Allowed: dismissed (use complete: true to mark one done).");
                if (string.IsNullOrWhiteSpace(resolution))
                    return McpToolHelpers.ErrorResult(
                        "Dismissing a task requires a resolution naming why it no longer applies.");
                if (entry.IsCompleted)
                    return McpToolHelpers.ErrorResult(
                        $"Task {entryId} is already complete; dismissing a finished task would overwrite the truer state.");
                ok = await svc.DismissTaskAsync(absolutePath, entryId, resolution);
                break;
            default:
                return McpToolHelpers.ErrorResult($"status is only valid on decision, block, or task entries; this entry is a {entry.Type.ToString().ToLowerInvariant()}.");
        }

        if (!ok)
            return McpToolHelpers.ErrorResult($"Entry not found with ID: {entryId}");

        _services.Logger!.Info($"MCP update_entry: {entryId} -> {status}");
        return McpToolHelpers.SuccessResult(new
        {
            Success = true,
            EntryId = entryId,
            EntryType = entry.Type.ToString().ToLowerInvariant(),
            Action = status,
            Path = Path.GetRelativePath(_services.VaultPath!, absolutePath),
            Resolution = entry.Type is EntryType.Block or EntryType.Task ? resolution : null,
        });
    }

    /// <summary>
    /// Locate an entry by ShortId across all vault markdown files, returning the
    /// ABSOLUTE file path (DecisionApprovalService reads the file directly).
    /// Adapted from EnrichEntryTool.FindEntryByIdAsync.
    /// </summary>
    private async Task<(string? Path, ParsedEntry? Entry)> FindEntryByIdAsync(string entryId, CancellationToken ct)
    {
        var mdFiles = McpToolHelpers.GetMarkdownFiles(_services.VaultPath!);
        foreach (var file in mdFiles)
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, ct);
            var entries = EntryParser.Parse(content, McpToolHelpers.GetTypeHintFromPath(file));
            foreach (var entry in entries)
            {
                var id = EntryResolver.GetEntryId(entry);
                if (id.Equals(entryId, StringComparison.OrdinalIgnoreCase))
                    return (file, entry);
            }
        }
        return (null, null);
    }
}
