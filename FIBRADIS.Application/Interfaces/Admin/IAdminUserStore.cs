using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models.Admin;

namespace FIBRADIS.Application.Interfaces.Admin;

public interface IAdminUserStore
{
    Task<(IReadOnlyList<AdminUser> Users, int TotalCount)> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<AdminUser?> GetByIdAsync(string userId, CancellationToken cancellationToken);
    Task<AdminUser> CreateAsync(AdminUserCreateRequest request, CancellationToken cancellationToken);
    Task<AdminUser> UpdateAsync(string userId, AdminUserUpdateRequest request, CancellationToken cancellationToken);
    Task DeactivateAsync(string userId, CancellationToken cancellationToken);
    Task<int> CountActiveAsync(CancellationToken cancellationToken);
}
