namespace CasCap.Models.Dtos;

/// <summary>The current resolved group names, without upstream group identifiers.</summary>
public sealed record SignalizrGroupsResponse(
    [property: Description("Exact configured Signal group names resolved on this process, preserving case and spaces; excludes group IDs.")]
    IReadOnlyList<string> Groups);
