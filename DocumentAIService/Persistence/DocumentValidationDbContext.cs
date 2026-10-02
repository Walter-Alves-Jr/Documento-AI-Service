using Microsoft.EntityFrameworkCore;

namespace DocumentAIService.Persistence;

public sealed class DocumentValidationDbContext(DbContextOptions<DocumentValidationDbContext> options) : DbContext(options)
{
    public DbSet<ComplianceEntity> Entities => Set<ComplianceEntity>();
    public DbSet<ComplianceDocument> Documents => Set<ComplianceDocument>();
    public DbSet<DocumentValidationRecord> DocumentValidations => Set<DocumentValidationRecord>();
    public DbSet<ExternalValidationRecord> ExternalValidations => Set<ExternalValidationRecord>();
    public DbSet<ValidationEvent> ValidationEvents => Set<ValidationEvent>();
    public DbSet<ExternalProviderConfiguration> ExternalProviderConfigurations => Set<ExternalProviderConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentValidationDbContext).Assembly);
}
