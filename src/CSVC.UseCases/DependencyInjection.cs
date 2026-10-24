using CSVC.UseCases.Accounts;
using CSVC.UseCases.Authentication;
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
        services.AddScoped<Departments.IDepartmentService, Departments.DepartmentService>();
        services.AddScoped<DeviceCategories.ICategoryService, DeviceCategories.CategoryService>();
        services.AddScoped<Areas.IAreaService, Areas.AreaService>();
        services.AddScoped<History.IHistoryService, History.HistoryService>();
        services.AddScoped<IAccountAuthenticationService, AccountAuthenticationService>();
        services.AddScoped<ISessionLifecycle, SessionLifecycle>();
        services.AddScoped<ISessionExpirationService, SessionExpirationService>();
        return services;
    }
}
