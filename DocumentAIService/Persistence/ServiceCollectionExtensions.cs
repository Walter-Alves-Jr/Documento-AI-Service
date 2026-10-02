using Microsoft.EntityFrameworkCore;

namespace DocumentAIService.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentValidationPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DocumentValidation")
            ?? throw new InvalidOperationException("A connection string 'DocumentValidation' é obrigatória.");

        services.AddDbContext<DocumentValidationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(DocumentValidationDbContext).Assembly.FullName)));
        return services;
    }
}
