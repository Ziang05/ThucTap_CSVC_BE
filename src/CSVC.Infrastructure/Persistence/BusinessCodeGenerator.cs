using System.Globalization;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence;

public sealed class BusinessCodeGenerator(CsvcDbContext context) : IBusinessCodeGenerator
{
    public Task<string> NextAccountCodeAsync(CancellationToken cancellationToken)
        => NextAsync("account_code_seq", "TK", true, cancellationToken);

    public Task<string> NextDepartmentCodeAsync(CancellationToken cancellationToken)
        => NextAsync("department_code_seq", "PB", false, cancellationToken);

    private async Task<string> NextAsync(string sequence, string prefix, bool account, CancellationToken ct)
    {
        while (true)
        {
            var number = await context.Database.SqlQuery<long>(
                $"SELECT nextval({sequence}::regclass) AS \"Value\"").SingleAsync(ct);
            var code = prefix + number.ToString("D6", CultureInfo.InvariantCulture);
            var key = code.ToLowerInvariant();
            var exists = account
                ? await context.Accounts.AnyAsync(a => EF.Property<string>(a, "NormalizedCode") == key, ct)
                : await context.Departments.AnyAsync(d => EF.Property<string>(d, "NormalizedCode") == key, ct);
            // A manually supplied code may already occupy a number in the generated range.
            if (!exists) return code;
        }
    }
}
