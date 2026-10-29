namespace LogMark.Services.Routes;

/// <summary>
/// A single node in the route tree. Unifies declared routes (### blocks in
/// routes.md) and larval scopes (entries in Route.Scopes with no ### block).
/// A scope is the larval, un-promoted form of a route -- a maturity state,
/// not a separate structure.
/// </summary>
public sealed record RouteNode
{
    /// <summary>Vault-relative path, e.g. "projects" or "projects/finetune-qwen-3".</summary>
    public required string Path { get; init; }

    /// <summary>Last path segment, e.g. "finetune-qwen-3".</summary>
    public required string Name { get; init; }

    /// <summary>True when this node exists only as a scope (no ### block yet).</summary>
    public required bool IsLarval { get; init; }

    /// <summary>
    /// True when this node is a synthetic grouping container: an implied
    /// ancestor path (e.g. "projects" or "domains") that no declared route or
    /// scope claims, but which has descendants. The route tree synthesizes
    /// these so deeply-nested routes whose top-level container is never
    /// declared still surface under a group row instead of vanishing. A
    /// container has no Route object and is not editable or promotable.
    /// </summary>
    public bool IsContainer { get; init; }

    /// <summary>Section (## header) of the owning declared route, if any. Larval nodes inherit their parent's.</summary>
    public string? Section { get; init; }

    /// <summary>Optional description from the ### block (null for larval).</summary>
    public string? Description { get; init; }
}
