using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentAIService.Persistence.Configurations;

internal sealed class ComplianceEntityConfiguration : IEntityTypeConfiguration<ComplianceEntity>
{
    public void Configure(EntityTypeBuilder<ComplianceEntity> builder)
    {
        builder.ToTable("entities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ClientId).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Cpf).HasMaxLength(1024);
        builder.Property(x => x.Plate).HasMaxLength(1024);
        builder.HasIndex(x => new { x.ClientId, x.Type, x.ExternalId }).IsUnique();
    }
}

internal sealed class ComplianceDocumentConfiguration : IEntityTypeConfiguration<ComplianceDocument>
{
    public void Configure(EntityTypeBuilder<ComplianceDocument> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DocumentNumber).HasMaxLength(1024);
        builder.Property(x => x.ExtractedDataSnapshot).HasMaxLength(16384);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.EntityId, x.DocumentType, x.FileHash }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextValidationAt });
        builder.HasOne(x => x.Entity).WithMany(x => x.Documents).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DocumentValidationRecordConfiguration : IEntityTypeConfiguration<DocumentValidationRecord>
{
    public void Configure(EntityTypeBuilder<DocumentValidationRecord> builder)
    {
        builder.ToTable("document_validations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ValidationId).HasMaxLength(40).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ValidationType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ResponseHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResponseSnapshot).HasMaxLength(16384);
        builder.Property(x => x.Provider).HasMaxLength(100);
        builder.Property(x => x.ClientId).HasMaxLength(160).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EstimatedCost).HasPrecision(18, 4);
        builder.HasIndex(x => new { x.ClientId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.ClientId, x.ValidationId }).IsUnique();
        builder.HasIndex(x => new { x.DocumentId, x.ValidatedAt });
        builder.HasOne(x => x.Document).WithMany(x => x.Validations).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExternalValidationRecordConfiguration : IEntityTypeConfiguration<ExternalValidationRecord>
{
    public void Configure(EntityTypeBuilder<ExternalValidationRecord> builder)
    {
        builder.ToTable("external_validations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Provider).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Response).HasMaxLength(16384);
        builder.Property(x => x.ReasonCode).HasMaxLength(100);
        builder.Property(x => x.EstimatedCost).HasPrecision(18, 4);
        builder.HasIndex(x => new { x.DocumentId, x.RequestedAt });
        builder.HasOne(x => x.Document).WithMany(x => x.ExternalValidations).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ValidationEventConfiguration : IEntityTypeConfiguration<ValidationEvent>
{
    public void Configure(EntityTypeBuilder<ValidationEvent> builder)
    {
        builder.ToTable("validation_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PreviousStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CurrentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReasonCode).HasMaxLength(100);
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.EntityId, x.CreatedAt });
        builder.HasOne(x => x.Entity).WithMany(x => x.Events).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Document).WithMany(x => x.Events).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DocumentValidationRecord).WithMany(x => x.Events).HasForeignKey(x => x.DocumentValidationRecordId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ExternalProviderConfigurationConfiguration : IEntityTypeConfiguration<ExternalProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<ExternalProviderConfiguration> builder)
    {
        builder.ToTable("external_provider_configurations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Endpoint).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.AuthenticationType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SecretReference).HasMaxLength(200);
        builder.Property(x => x.HeadersJson).HasMaxLength(8192).IsRequired();
        builder.Property(x => x.ParametersJson).HasMaxLength(8192).IsRequired();
        builder.Property(x => x.EstimatedCost).HasPrecision(18, 4);
        builder.HasIndex(x => new { x.Code, x.DocumentType }).IsUnique();
        builder.HasIndex(x => new { x.DocumentType, x.Active });
    }
}
