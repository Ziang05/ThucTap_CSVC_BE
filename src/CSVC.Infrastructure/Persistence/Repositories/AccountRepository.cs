using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.Core.Accounts;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Accounts.Models;
using CSVC.UseCases.Common;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<Account>(context), IAccountRepository
{
    public Task<bool> CodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var key = code.Trim().ToLowerInvariant();
        return _dbSet.AnyAsync(a => a.Id != exceptId && EF.Property<string>(a, "NormalizedCode") == key, cancellationToken);
    }

    public Task<Account?> GetByNormalizedEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = TextNormalization.Email(email);
        return FirstOrDefaultAsync(account => EF.Property<string>(account, "NormalizedEmail") == normalizedEmail, cancellationToken);
    }

    public async Task<PagedResult<AccountListItemDto>> ListAsync(AccountListQuery query, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var source = ReadAccounts(now);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(account => account.FullName.ToLower().Contains(search));
        }
        if (query.RoleId is { } roleId) source = source.Where(account => account.RoleId == roleId);
        if (query.IsLocked is { } locked) source = source.Where(account => account.IsLocked == locked);
        if (query.DepartmentIds.Length > 0)
        {
            var departmentIds = query.DepartmentIds.Distinct().ToArray();
            source = source.Where(account => departmentIds.Contains(account.DepartmentId));
        }
        var count = await source.CountAsync(cancellationToken);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            (AccountSortBy.CreatedAt, SortDirection.Asc) => source.OrderBy(account => account.CreatedAt),
            (AccountSortBy.CreatedAt, SortDirection.Desc) => source.OrderByDescending(account => account.CreatedAt),
            (AccountSortBy.FullName, SortDirection.Desc) => source.OrderByDescending(account => account.FullName),
            _ => source.OrderBy(account => account.FullName)
        };
        var items = await ordered.ThenBy(account => account.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ProjectTo<AccountListItemDto>(mapper.ConfigurationProvider).ToListAsync(cancellationToken);
        return new(items, count, query.Page, query.PageSize);
    }

    public Task<AccountDetailDto?> GetDetailAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default)
        => ReadAccounts(now).Where(account => account.Id == id)
            .ProjectTo<AccountDetailDto>(mapper.ConfigurationProvider).SingleOrDefaultAsync(cancellationToken);

    public Task<int> CountActiveManagersAsync(CancellationToken cancellationToken = default)
        => (from account in _context.Accounts
            join role in _context.Roles on account.RoleId equals role.Id
            where !account.IsLock && role.Code == RoleCodes.EquipmentManager
            select account.Id).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<AssignableAccountDto>> ListAssignableAsync(Guid departmentId, CancellationToken cancellationToken = default)
        => await (from account in _context.Accounts.AsNoTracking()
                  join profile in _context.UserProfiles.AsNoTracking() on account.UserProfileId equals profile.Id
                  where profile.DepartmentId == departmentId && !account.IsLock
                  orderby profile.FullName, account.AccountCode
                  select new AssignableAccountDto(account.AccountCode, profile.FullName))
            .ToListAsync(cancellationToken);

    private IQueryable<AccountReadModel> ReadAccounts(DateTimeOffset now)
        => from account in _context.Accounts.AsNoTracking()
           join profile in _context.UserProfiles on account.UserProfileId equals profile.Id
           join role in _context.Roles on account.RoleId equals role.Id
           join department in _context.Departments on profile.DepartmentId equals department.Id
           select new AccountReadModel
           {
               Id = account.Id, AccountCode = account.AccountCode, Email = account.Email,
               FullName = profile.FullName, BirthDate = profile.BirthDate, Phone = profile.Phone,
               RoleId = role.Id, RoleCode = role.Code, RoleName = role.DisplayName,
               DepartmentId = department.Id, DepartmentName = department.Name, IsLocked = account.IsLock,
               HasValidSession = !account.IsLock && _context.AuthSessions.Any(session => session.AccountId == account.Id
                   && session.IsActive && session.ExpiresAt > now),
               CreatedAt = _context.AccountHistories.Where(history => history.TargetAccountId == account.Id
                       && history.ActionCode == "ACCOUNT_CREATED" && history.Outcome == AuditOutcome.Success)
                   .OrderBy(history => history.OccurredAt).Select(history => (DateTimeOffset?)history.OccurredAt).FirstOrDefault(),
               CreatedBy = _context.AccountHistories.Where(history => history.TargetAccountId == account.Id
                       && history.ActionCode == "ACCOUNT_CREATED" && history.Outcome == AuditOutcome.Success)
                   .OrderBy(history => history.OccurredAt).Select(history => history.ActorAccountId).FirstOrDefault()
           };
}
