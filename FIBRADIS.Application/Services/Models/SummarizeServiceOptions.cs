namespace FIBRADIS.Application.Services.Models;

public sealed class SummarizeServiceOptions
{
    public string Provider { get; set; } = "openai";
    public string SystemKey { get; set; } = string.Empty;
    public decimal CostPerThousandTokensUsd { get; set; } = 0.0025m;
    public IReadOnlyList<string> EventKeywords { get; set; } = new[] { "distribución", "expansión", "refinanciamiento", "adquisición" };
    public int MinFactsRequired { get; set; } = 2;
}
