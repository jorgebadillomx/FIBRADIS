using System;

namespace FIBRADIS.Api.Monitoring;

public sealed class MonitoringOptions
{
    public ApiOptions Api { get; set; } = new();
    public AuthenticationOptions Authentication { get; set; } = new();
    public OtlpOptions Otlp { get; set; } = new();

    public sealed class ApiOptions
    {
        public int PublicLatencyThresholdMs { get; set; } = 800;
        public string PublicPathPrefix { get; set; } = "/v1/";
    }

    public sealed class AuthenticationOptions
    {
        public string[] ProbeTokens { get; set; } = Array.Empty<string>();
    }

    public sealed class OtlpOptions
    {
        public bool Enabled { get; set; } = true;
        public string? Endpoint { get; set; }
        public bool UseTls { get; set; } = true;
    }
}
