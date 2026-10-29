using LogMark.Interfaces;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.Sensitivity;
using LogMark.Services.Pages;

namespace LogMark.Mcp;

/// <summary>
/// Mutable holder for all vault-dependent services.
/// Tools access services through this holder. On vault switch,
/// properties are updated under lock.
///
/// This open base carries only the kernel service graph. The closed
/// LogMark.Mcp.Producers assembly derives <c>ProducerVaultServices</c> to add
/// the Core-backed accessors (image assets, feed, protocols, views) its closed
/// tools consume; open tools never see them.
/// </summary>
public class VaultServices
{
    public string? VaultPath { get; set; }
    public IVaultFileWriter? VaultWriter { get; set; }
    public IVaultWriteGate? VaultWriteGate { get; set; }
    public VaultService? VaultService { get; set; }
    public NotationParser? NotationParser { get; set; }
    public RouteService? RouteService { get; set; }
    public SystemLogger? Logger { get; set; }
    public EntryCache? EntryCache { get; set; }
    public SearchService? SearchService { get; set; }
    public EntryResolver? EntryResolver { get; set; }
    public SessionService? SessionService { get; set; }
    public McpSessionContext? SessionContext { get; set; }
    public TaskTracker? TaskTracker { get; set; }
    public SubpageWriter? SubpageWriter { get; set; }
    public EntryPageLinker? EntryPageLinker { get; set; }

    /// <summary>
    /// T1 semantic search. Kernel interface; the concrete embedding-backed
    /// implementation is wired only by the closed producer, so this is null in
    /// the open build and search degrades to lexical-only.
    /// </summary>
    public ISemanticSearchService? SemanticSearchService { get; set; }
}
