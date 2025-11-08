namespace FIBRADIS.Application.Services.Models;

public sealed class NewsClassifierOptions
{
    public IReadOnlyDictionary<string, string[]> FibraKeywords { get; init; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["FUNO11"] = new[] { "funo", "fibra uno" },
        ["TERRA13"] = new[] { "fibra terra", "terra" },
        ["FIBRAPL"] = new[] { "fibra plus", "fibrapl" }
    };

    public IReadOnlyDictionary<string, string[]> SectorKeywords { get; init; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Industrial"] = new[] { "industrial", "logística", "almacén" },
        ["Retail"] = new[] { "comercial", "retail", "centro comercial" },
        ["Hotel"] = new[] { "hotel", "hospitality", "turismo" }
    };

    public IReadOnlyList<string> PositiveKeywords { get; init; } = new[] { "crecimiento", "expansión", "récord", "aumento" };
    public IReadOnlyList<string> NegativeKeywords { get; init; } = new[] { "caída", "reducción", "problema", "disminución", "conflicto" };
    public string Provider { get; init; } = "openai";
    public decimal CostPerThousandTokensUsd { get; init; } = 0.0015m;
}
