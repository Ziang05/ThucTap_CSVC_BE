using CSVC.Core.Authentication;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AuthHistoryRepository(CsvcDbContext context)
    : GenericRepository<AuthHistory>(context), IAuthHistoryRepository
{
}
