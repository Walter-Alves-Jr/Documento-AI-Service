using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocumentAIService.Persistence;

public sealed class DocumentValidationDbContextFactory : IDesignTimeDbContextFactory<DocumentValidationDbContext>
{
    public DocumentValidationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DocumentValidation")
            ?? "Host=localhost;Port=5432;Database=document_validation;Username=document_validation";
        var options = new DbContextOptionsBuilder<DocumentValidationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(DocumentValidationDbContext).Assembly.FullName))
            .Options;
        return new DocumentValidationDbContext(options);
    }
}
