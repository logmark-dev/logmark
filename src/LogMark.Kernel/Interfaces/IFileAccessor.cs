using System.IO;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Abstraction for file system operations, enabling unit testing of cache logic.
/// </summary>
public interface IFileAccessor
{
    string ReadAllText(string path);
    void WriteAllText(string path, string content);

    /// <summary>
    /// True append: writes content to the end of the file without reading
    /// or rewriting anything already there, so a concurrent writer's own
    /// append (or a reader that consumes-then-clears the file) is never
    /// clobbered or resurrected. Creates the file if it does not exist.
    /// Default implementation delegates to File.AppendAllText - prefer this
    /// over ReadAllText + WriteAllText(existing + line) for any append-only
    /// file (e.g. the resident room's inbox bits.jsonl).
    /// </summary>
    void AppendAllText(string path, string content) => File.AppendAllText(path, content);

    /// <summary>Atomic replace for small state files: write a temp file, then move it over the target.</summary>
    void MoveFile(string from, string to, bool overwrite) => File.Move(from, to, overwrite);

    bool Exists(string path);
    DateTime GetLastWriteTimeUtc(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option);

    /// <summary>
    /// Immediate child directories of path (not recursive). Used where a
    /// caller needs to check each child's own root without walking the
    /// full subtree beneath it (e.g. avoiding a nested .git directory).
    /// </summary>
    IEnumerable<string> EnumerateDirectories(string path);

    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    bool FileExists(string path);
    void DeleteFile(string path);

    /// <summary>
    /// Size of the file in bytes. Returns 0 when the file does not exist.
    /// </summary>
    long GetFileSize(string path);
}
