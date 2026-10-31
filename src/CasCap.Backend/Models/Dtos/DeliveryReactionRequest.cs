namespace CasCap.Models.Dtos;

/// <summary>Sets or removes a reaction on a delivered message, addressed by its delivery identifier.</summary>
public sealed record DeliveryReactionRequest
{
    /// <summary>The reaction emoji.</summary>
    [Required, MinLength(1)]
    public required string Reaction { get; init; }
}
