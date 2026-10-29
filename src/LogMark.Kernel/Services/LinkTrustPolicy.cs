using System.IO;
using LogMark.Services.EntryCache;

namespace LogMark.Services;

public enum LinkTargetKind { VaultInternal, LocalOpen, LocalExecutable, External }

public sealed record LinkTarget(LinkTargetKind Kind, string Value, string? Domain = null);

/// <summary>
/// Tiered link trust classification (design: 2026-08-17-entry-links-and-quick-actions-design.md).
/// Internal -> navigate in-viewer; LocalOpen -> silent shell open; LocalExecutable ->
/// confirm even locally (entries contain agent-written content); External -> per-domain confirm.
/// Pure classification only - dialogs and launching are App concerns.
/// </summary>
public static class LinkTrustPolicy
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".psm1", ".lnk", ".vbs", ".vbe",
        ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".com", ".pif", ".hta", ".jar",
        ".url", ".cpl", ".reg", ".msix", ".appx",
        ".py", ".pyw", ".ahk", ".sct", ".ws"
    };

    public static LinkTarget ClassifyUrl(string url)
    {
        string? domain = null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            domain = uri.Scheme is "http" or "https" ? uri.Host : uri.Scheme;
        return new LinkTarget(LinkTargetKind.External, url, domain);
    }

    public static LinkTarget ClassifyPath(string rawPath, string? vaultPath)
    {
        var path = rawPath;
        if (rawPath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(rawPath, UriKind.Absolute, out var fileUri))
            path = fileUri.LocalPath;

        // Executable extension is checked BEFORE vault membership. The vault
        // is agent-writable - an agent or a pasted peer instruction can drop
        // a .cmd/.exe next to a note just as easily as outside the vault - so
        // vault membership is NOT a trust boundary for executables. A
        // vault-internal .exe must classify as LocalExecutable (confirm even
        // locally) rather than VaultInternal, or App-layer dispatch logic
        // that trusts VaultInternal by default would launch it unconfirmed.
        var ext = Path.GetExtension(path);
        if (ExecutableExtensions.Contains(ext))
            return new LinkTarget(LinkTargetKind.LocalExecutable, path);

        if (LinkTargetResolver.IsUnderRoot(path, vaultPath))
            return new LinkTarget(LinkTargetKind.VaultInternal, path);

        return new LinkTarget(LinkTargetKind.LocalOpen, path);
    }
}
