using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NotificationAuditDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'NotificationAuditDatabase' is missing.");
        }

        services.AddDbContext<NotificationAuditDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
