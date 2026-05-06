namespace Philche.Core.Domain.Models;

public sealed record FieldProvenance(
    string Field,
    string Source,
    string? SourceId = null);
