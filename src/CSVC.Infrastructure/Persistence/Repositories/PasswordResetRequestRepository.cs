using CSVC.Core.Accounts;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetRequestRepository(CsvcDbContext context)
    : GenericRepository<PasswordResetRequest>(context), IPasswordResetRequestRepository
{
}
