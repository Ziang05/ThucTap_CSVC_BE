using CSVC.Core.Authentication;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AuthLoginThrottleRepository(CsvcDbContext context)
    : GenericRepository<AuthLoginThrottle>(context), IAuthLoginThrottleRepository
{
}
