// src/LogMark.Core/Services/Export/RouteArchiveResult.cs
namespace LogMark.Services.Export;

public sealed record RouteArchiveResult(
    string ArchivePath,
    int EntriesCount,
    IReadOnlyList<string> MissingAssets);
