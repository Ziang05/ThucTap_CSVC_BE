using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Accounts;

public interface IAccountService
{
    Task<PagedResult<AccountListItemDto>> ListAsync(AccountListQuery query, CancellationToken cancellationToken = default);
    Task<AccountDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AccountDetailDto> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<CreatedAccountDto> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(Guid id, UpdateAccountProfileRequest request, CancellationToken cancellationToken = default);
    Task UpdateCurrentProfileAsync(UpdateAccountProfileRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task SetPasswordAsync(Guid id, SetAccountPasswordRequest request, CancellationToken cancellationToken = default);
    Task SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken = default);
    Task CompletePasswordResetAsync(CompletePasswordResetRequest request, CancellationToken cancellationToken = default);
}
