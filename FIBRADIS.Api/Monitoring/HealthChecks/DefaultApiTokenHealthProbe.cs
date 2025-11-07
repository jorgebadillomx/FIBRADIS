using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Infrastructure.Observability.Health;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FIBRADIS.Api.Monitoring.HealthChecks;

public sealed class DefaultApiTokenHealthProbe : IApiTokenHealthProbe
{
    private readonly MonitoringOptions _options;
    private readonly ILogger<DefaultApiTokenHealthProbe> _logger;

    public DefaultApiTokenHealthProbe(IOptions<MonitoringOptions> options, ILogger<DefaultApiTokenHealthProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken)
    {
        var tokens = _options.Authentication.ProbeTokens;
        if (tokens is null || tokens.Length == 0)
        {
            return Task.FromResult(new HealthProbeResult(false, "No hay tokens configurados para la API privada."));
        }

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var parsed = ParseToken(token);
            if (!string.IsNullOrWhiteSpace(parsed.UserId))
            {
                return Task.FromResult(new HealthProbeResult(true, "Tokens configurados correctamente."));
            }
        }

        _logger.LogWarning("Ninguno de los tokens de prueba contiene un identificador de usuario válido.");
        return Task.FromResult(new HealthProbeResult(false, "Tokens configurados pero inválidos."));
    }

    private static (string? UserId, string? Role) ParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return default;
        }

        if (token.Contains(':', StringComparison.Ordinal))
        {
            var segments = token.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in segments)
            {
                var parts = segment.Split(':', 2);
                if (parts.Length == 2)
                {
                    dictionary[parts[0]] = parts[1];
                }
            }

            dictionary.TryGetValue("sub", out var sub);
            dictionary.TryGetValue("user", out var user);
            dictionary.TryGetValue("role", out var role);
            return (user ?? sub, role);
        }

        return (token.Trim(), null);
    }
}
