namespace CasCap.Models.Dtos;

/// <summary>The upstream acknowledgement for one text-send request.</summary>
public sealed record SignalizrMcpSendResponse(
    [property: Description("Message timestamp acknowledged by the Signal wrapper; not proof of delivery or reading by group members.")]
    string Timestamp);
