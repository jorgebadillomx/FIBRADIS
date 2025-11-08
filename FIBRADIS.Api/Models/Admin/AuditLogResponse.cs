using System;
using System.Collections.Generic;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Api.Models.Admin;

public sealed record AuditLogResponse(
    Guid Id,
    string UserId,
    string Action,
    string Result,
    DateTime TimestampUtc,
    string IpAddress,
    IReadOnlyDictionary<string, object?> Metadata)
{
    public static AuditLogResponse FromModel(AuditLog log)
    {
        return new AuditLogResponse(
            log.Id,
            log.UserId,
            log.Action,
            log.Result,
            log.TimestampUtc,
            log.IpAddress,
            log.Metadata);
    }
}
