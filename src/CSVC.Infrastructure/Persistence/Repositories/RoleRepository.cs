using CSVC.Core.Accounts;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class RoleRepository(CsvcDbContext context)
    : GenericRepository<Role>(context), IRoleRepository
{
}
