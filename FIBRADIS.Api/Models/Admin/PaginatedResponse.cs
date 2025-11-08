using System.Collections.Generic;

namespace FIBRADIS.Api.Models.Admin;

public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
