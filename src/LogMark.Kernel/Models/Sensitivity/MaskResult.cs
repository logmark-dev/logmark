namespace LogMark.Models.Sensitivity;

public record MaskedToken(
    string TokenId,
    string EntityType,
    string OriginalValue
);

public record MaskResult(
    string MaskedText,
    IReadOnlyList<MaskedToken> Tokens
);
