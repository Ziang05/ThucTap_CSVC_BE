using CSVC.Core.Common;
using CSVC.Infrastructure.Health;
using CSVC.Infrastructure.Persistence;
using CSVC.Infrastructure.Persistence.Repositories;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings:Database using user-secrets or environment variables.");
        }

        // Parse configuration now; never include credentials in errors or logs.
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.Host)
            || string.IsNullOrWhiteSpace(connection.Database)
            || string.IsNullOrWhiteSpace(connection.Username))
        {
            throw new InvalidOperationException("Database connection requires Host, Database and Username.");
        }

        services.AddDbContext<CsvcDbContext>(options => options.UseNpgsql(
            connectionString,
            postgres => postgres.CommandTimeout(30)
                .MapEnum<EntityStatus>("entity_status", nameTranslator: new DatabaseEnumNameTranslator())
                .MapEnum<AuditOutcome>("audit_outcome", nameTranslator: new DatabaseEnumNameTranslator())));
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IDepartmentHistoryRepository, DepartmentHistoryRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountHistoryRepository, AccountHistoryRepository>();
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
        services.AddScoped<IAuthHistoryRepository, AuthHistoryRepository>();
        services.AddScoped<IAuthLoginThrottleRepository, AuthLoginThrottleRepository>();
        services.AddScoped<IPasswordResetRequestRepository, PasswordResetRequestRepository>();
        services.AddScoped<IAreaRepository, AreaRepository>();
        services.AddScoped<IAreaHistoryRepository, AreaHistoryRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(
            "postgresql", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));

        return services;
    }
}
