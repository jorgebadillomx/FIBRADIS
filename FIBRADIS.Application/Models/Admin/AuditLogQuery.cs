using System;

namespace FIBRADIS.Application.Models.Admin;

public sealed record AuditLogQuery(string? UserId, string? Action, DateTime? FromUtc, DateTime? ToUtc, int Page, int PageSize);
