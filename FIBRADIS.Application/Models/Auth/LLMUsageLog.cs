using System;

namespace FIBRADIS.Application.Models.Auth;

public sealed class LLMUsageLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string UserId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public int TokensUsed { get; init; }
    public decimal Cost { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
