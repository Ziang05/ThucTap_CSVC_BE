using CSVC.UseCases.Accounts;
using CSVC.UseCases.Accounts.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CSVC.UseCases;

public static class DependencyInjection
{
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddAutoMapper(_ => { }, typeof(AccountMappingProfile));
        services.AddScoped<IAccountService, AccountService>();
        return services;
    }
}
