namespace CasCap.Models.Dtos;

/// <summary>A bounded slice of persisted inbound messages for one resolved group.</summary>
public sealed record SignalizrMcpMessageHistoryResponse(
    [property: Description("The exact Signal group name, including case and spaces.")]
    string GroupName,
    [property: Description("Persisted inbound messages, newest persisted first; not a complete Signal conversation or a record of every outbound send.")]
    IReadOnlyList<SignalizrMcpMessageResponse> Messages);
