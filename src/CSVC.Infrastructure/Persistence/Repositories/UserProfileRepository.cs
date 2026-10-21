using CSVC.Core.Accounts;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class UserProfileRepository(CsvcDbContext context)
    : GenericRepository<UserProfile>(context), IUserProfileRepository
{
}
