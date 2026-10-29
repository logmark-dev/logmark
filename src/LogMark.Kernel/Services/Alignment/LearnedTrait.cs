namespace LogMark.Services.Alignment;

public enum TraitStatus { Confirmed, Retired }

/// <summary>A learned preference, materialized as a +self entry (alignment-loop-v1 design sec 5).</summary>
public sealed record LearnedTrait(
    string Id, string Rule, string Scope, TraitStatus Status, DateTime StatusDate,
    int EvidenceCount, string EvidenceLine, string ReasonsLine,
    double? BaselineDiscardRate, IReadOnlyList<string> Examples);
