using LogMark.Services.EntryCache;

namespace LogMark.TestUtilities;

/// <summary>
/// In-memory file system for unit testing EntryCache.
/// </summary>
public class MockFileAccessor : IFileAccessor
{
    private readonly Dictionary<string, MockFile> _files = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, string content, DateTime? lastModified = null)
    {
        var normalizedPath = Path.GetFullPath(path);
        _files[normalizedPath] = new MockFile(content, lastModified ?? DateTime.UtcNow);
    }

    public void UpdateFile(string path, string content)
    {
        var normalizedPath = Path.GetFullPath(path);
        if (_files.TryGetValue(normalizedPath, out var existing))
        {
            _files[normalizedPath] = new MockFile(content, DateTime.UtcNow);
        }
    }

    public void TouchFile(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        if (_files.TryGetValue(normalizedPath, out var existing))
        {
            _files[normalizedPath] = new MockFile(existing.Content, DateTime.UtcNow);
        }
    }

    public void DeleteFile(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        _files.Remove(normalizedPath);
    }

    public void WriteAllText(string path, string content)
    {
        var normalizedPath = Path.GetFullPath(path);
        _files[normalizedPath] = new MockFile(content, DateTime.UtcNow);
    }

    /// <summary>
    /// In-memory move (mirrors FileAccessor.MoveFile): the default interface
    /// implementation would call File.Move on the real disk, where the mock's
    /// files do not exist.
    /// </summary>
    public void MoveFile(string from, string to, bool overwrite)
    {
        var src = Path.GetFullPath(from);
        var dst = Path.GetFullPath(to);
        if (!_files.TryGetValue(src, out var file))
            throw new FileNotFoundException($"File not found: {from}");
        if (!overwrite && _files.ContainsKey(dst))
            throw new IOException($"File already exists: {to}");
        _files[dst] = file;
        _files.Remove(src);
    }

    /// <summary>
    /// True append (mirrors FileAccessor.AppendAllText): concatenates onto
    /// existing content when the file is already tracked, otherwise creates
    /// it - never reads the file back through WriteAllText's replace path.
    /// </summary>
    public void AppendAllText(string path, string content)
    {
        var normalizedPath = Path.GetFullPath(path);
        var existing = _files.TryGetValue(normalizedPath, out var file) ? file.Content : string.Empty;
        _files[normalizedPath] = new MockFile(existing + content, DateTime.UtcNow);
    }

    public string ReadAllText(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
            return file.Content;
        throw new FileNotFoundException($"File not found: {path}");
    }

    public bool Exists(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return _files.ContainsKey(normalizedPath);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
            return file.LastModified;
        throw new FileNotFoundException($"File not found: {path}");
    }

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option)
    {
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        return _files.Keys
            .Where(k => k.ToLowerInvariant().StartsWith(normalizedPath))
            .Where(k => MatchesPattern(k, pattern));
    }

    public bool DirectoryExists(string path)
    {
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        return _files.Keys.Any(k => k.ToLowerInvariant().StartsWith(normalizedPath));
    }

    /// <summary>
    /// Immediate child directories of path, derived from tracked file paths
    /// (the mock has no separate directory concept - a directory "exists"
    /// only implicitly, via files beneath it).
    /// </summary>
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = normalizedPath + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _files.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = key[prefix.Length..];
            var sepIndex = rest.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            if (sepIndex < 0)
                continue; // file lives directly in path, not in a subdirectory

            var childName = rest[..sepIndex];
            seen.Add(normalizedPath + Path.DirectorySeparatorChar + childName);
        }

        return seen;
    }

    public void CreateDirectory(string path)
    {
        // No-op: mock tracks files, not directories
    }

    public bool FileExists(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return _files.ContainsKey(normalizedPath);
    }

    public long GetFileSize(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return _files.TryGetValue(normalizedPath, out var file) ? file.Content.Length : 0;
    }

    // DeleteFile already exists above as a public method and satisfies the interface

    private static bool MatchesPattern(string path, string pattern)
    {
        if (pattern == "*.*" || pattern == "*")
            return true;

        var fileName = Path.GetFileName(path);

        // Treat pattern as a glob with `*` as the only wildcard. Splitting on `*`
        // yields literal segments that must appear in order, with the first segment
        // anchored to the start (unless empty) and the last to the end (unless empty).
        // Examples:
        //   "*.md"                -> ["", ".md"]: ends with ".md"
        //   "2026-02-22-foo-*.md" -> ["2026-02-22-foo-", ".md"]: starts and ends
        //   "*-depth-*.md"        -> ["", "-depth-", ".md"]: contains "-depth-", ends with ".md"
        var segments = pattern.Split('*');
        if (segments.Length == 1)
            return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);

        var pos = 0;

        // First segment anchored to start
        if (segments[0].Length > 0)
        {
            if (!fileName.StartsWith(segments[0], StringComparison.OrdinalIgnoreCase))
                return false;
            pos = segments[0].Length;
        }

        // Middle segments must appear in order
        for (int i = 1; i < segments.Length - 1; i++)
        {
            if (segments[i].Length == 0) continue;
            var idx = fileName.IndexOf(segments[i], pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            pos = idx + segments[i].Length;
        }

        // Last segment anchored to end (and must not overlap previously matched region)
        var last = segments[^1];
        if (last.Length > 0)
        {
            if (!fileName.EndsWith(last, StringComparison.OrdinalIgnoreCase))
                return false;
            if (fileName.Length - last.Length < pos)
                return false;
        }

        return true;
    }

    private record MockFile(string Content, DateTime LastModified);
}
