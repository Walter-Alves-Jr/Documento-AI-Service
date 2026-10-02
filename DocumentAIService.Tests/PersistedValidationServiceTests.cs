using DocumentAIService.Configuration;
using DocumentAIService.Models.V1;
using DocumentAIService.Persistence;
using DocumentAIService.Security;
using DocumentAIService.Services.V1;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Tests;

public sealed class PersistedValidationServiceTests
{
    [Fact]
    public async Task Reuses_persisted_validation_for_same_entity_document_type_and_hash()
    {
        await using var db = CreateDb();
        var internalValidation = new FakeInternalValidationService();
        var service = CreateService(db, internalValidation);
        var request = Request("VEHICLE-001", ComplianceEntityType.VEHICLE, "CIPP", "CIPP_DEFAULT", "ABC-1D23");

        var first = await service.ValidateAsync(request, [1, 2, 3], "image/png", "consumer-a", "corr-1", CancellationToken.None);
        var second = await service.ValidateAsync(request, [1, 2, 3], "image/png", "consumer-a", "corr-2", CancellationToken.None);

        Assert.False(first.CacheUsed);
        Assert.True(second.CacheUsed);
        Assert.Equal(first.ValidationId, second.ValidationId);
        Assert.Equal(1, internalValidation.Calls);
        Assert.Equal(1, await db.Documents.CountAsync());
        Assert.Equal(1, await db.DocumentValidations.CountAsync());
        Assert.Equal(ExternalValidationStatus.NOT_AVAILABLE, await db.ExternalValidations.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Configured_provider_without_adapter_leaves_document_pending_without_network_call()
    {
        await using var db = CreateDb();
        db.ExternalProviderConfigurations.Add(new ExternalProviderConfiguration
        {
            Code = "ANTT",
            DocumentType = "CIPP",
            Endpoint = "https://provider.example/validate",
            HttpMethod = "GET",
            AuthenticationType = "bearer",
            SecretReference = "ANTT_API_TOKEN",
            TimeoutSeconds = 15,
            TtlDays = 15,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeInternalValidationService());

        var result = await service.ValidateAsync(Request("VEHICLE-002", ComplianceEntityType.VEHICLE, "CIPP", "CIPP_DEFAULT", "ABC-1D23"), [4, 5, 6], "image/png", "consumer-a", "corr-1", CancellationToken.None);

        Assert.Equal(ComplianceStatus.PENDING_VALIDATION, result.ComplianceStatus);
        Assert.Equal("EXTERNAL_PROVIDER_UNAVAILABLE", result.ComplianceReason);
        Assert.False(result.ExternalCall);
        Assert.Equal("ANTT", result.Provider);
        var call = await db.ExternalValidations.SingleAsync();
        Assert.Equal(ExternalValidationStatus.UNAVAILABLE, call.Status);
        Assert.Equal(0, call.EstimatedCost);
    }

    [Fact]
    public async Task Marks_cross_document_plate_mismatch_as_pending_by_default()
    {
        await using var db = CreateDb();
        var internalValidation = new FakeInternalValidationService();
        var service = CreateService(db, internalValidation);

        await service.ValidateAsync(Request("VEHICLE-003", ComplianceEntityType.VEHICLE, "CIPP", "CIPP_DEFAULT", "ABC-1D23"), [7], "image/png", "consumer-a", "corr-1", CancellationToken.None);
        var second = await service.ValidateAsync(Request("VEHICLE-003", ComplianceEntityType.VEHICLE, "CRLV", "CRLV_DEFAULT", "ABC-1D24"), [8], "image/png", "consumer-a", "corr-2", CancellationToken.None);

        Assert.Equal(ComplianceStatus.PENDING_VALIDATION, second.ComplianceStatus);
        Assert.Equal("CROSS_DOCUMENT_PLATE_MISMATCH", second.ComplianceReason);
    }

    [Fact]
    public async Task Compliance_query_reads_persisted_state_without_new_validation()
    {
        await using var db = CreateDb();
        var internalValidation = new FakeInternalValidationService();
        var service = CreateService(db, internalValidation);
        await service.ValidateAsync(Request("DRIVER-001", ComplianceEntityType.DRIVER, "CNH", "CNH_DEFAULT", null), [9], "image/png", "consumer-a", "corr-1", CancellationToken.None);

        var summary = await service.GetComplianceAsync(ComplianceEntityType.DRIVER, "DRIVER-001", "consumer-a", CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(ComplianceStatus.APPROVED, summary!.Status);
        Assert.Single(summary.Documents);
        Assert.Equal(1, internalValidation.Calls);
    }

    [Fact]
    public async Task Changed_file_hash_requires_new_internal_validation()
    {
        await using var db = CreateDb();
        var internalValidation = new FakeInternalValidationService();
        var service = CreateService(db, internalValidation);
        var request = Request("VEHICLE-004", ComplianceEntityType.VEHICLE, "CIPP", "CIPP_DEFAULT", "ABC-1D23");

        await service.ValidateAsync(request, [1, 2, 3], "image/png", "consumer-a", "corr-1", CancellationToken.None);
        var changed = await service.ValidateAsync(request, [3, 2, 1], "image/png", "consumer-a", "corr-2", CancellationToken.None);

        Assert.False(changed.CacheUsed);
        Assert.Equal(2, internalValidation.Calls);
        Assert.Equal(2, await db.Documents.CountAsync());
    }

    [Fact]
    public async Task Idempotency_key_returns_original_validation_for_same_client()
    {
        await using var db = CreateDb();
        var internalValidation = new FakeInternalValidationService();
        var service = CreateService(db, internalValidation);
        var request = Request("VEHICLE-006", ComplianceEntityType.VEHICLE, "CIPP", "CIPP_DEFAULT", "ABC-1D23");
        request.IdempotencyKey = "vehicle-006-request-1";

        var first = await service.ValidateAsync(request, [1], "image/png", "consumer-a", "corr-1", CancellationToken.None);
        var second = await service.ValidateAsync(request, [2], "image/png", "consumer-a", "corr-2", CancellationToken.None);

        Assert.True(second.CacheUsed);
        Assert.Equal(first.ValidationId, second.ValidationId);
        Assert.Equal(1, internalValidation.Calls);
    }

    [Fact]
    public async Task Renewal_marks_only_documents_with_due_external_validation()
    {
        await using var db = CreateDb();
        var entity = new ComplianceEntity { ClientId = "consumer-a", Type = ComplianceEntityType.VEHICLE, ExternalId = "VEHICLE-005", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var due = new ComplianceDocument { Entity = entity, DocumentType = "CIPP", FileHash = new string('a', 64), FileName = "a.png", MimeType = "image/png", Status = ComplianceStatus.APPROVED, NextValidationAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var future = new ComplianceDocument { Entity = entity, DocumentType = "CRLV", FileHash = new string('b', 64), FileName = "b.png", MimeType = "image/png", Status = ComplianceStatus.APPROVED, NextValidationAt = DateTimeOffset.UtcNow.AddDays(20), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Documents.AddRange(due, future);
        await db.SaveChangesAsync();
        var settings = new DvsOptions();
        settings.Renewal.LeadDays = 3;
        var service = CreateService(db, new FakeInternalValidationService(), settings);

        var marked = await service.MarkExternalValidationsDueAsync(CancellationToken.None);

        Assert.Equal(1, marked);
        Assert.Equal(ComplianceStatus.PENDING_VALIDATION, due.Status);
        Assert.Equal(ComplianceStatus.APPROVED, future.Status);
        Assert.Equal("EXTERNAL_VALIDATION_EXPIRED", await db.ValidationEvents.Select(x => x.ReasonCode).SingleAsync());
    }

    private static PersistedValidationRequest Request(string entityId, ComplianceEntityType type, string documentType, string policy, string? plate) => new()
    {
        EntityId = entityId,
        EntityType = type,
        DocumentType = documentType,
        Policy = policy,
        File = "ignored",
        FileName = "synthetic.png",
        Plate = plate
    };

    private static DocumentValidationDbContext CreateDb() => new(new DbContextOptionsBuilder<DocumentValidationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .Options);

    private static PersistedValidationService CreateService(DocumentValidationDbContext db, FakeInternalValidationService internalValidation, DvsOptions? settings = null)
    {
        var registrations = new ServiceCollection();
        registrations.AddDataProtection();
        var services = registrations.BuildServiceProvider();
        var codec = new ProtectedDataCodec(services.GetRequiredService<IDataProtectionProvider>());
        return new PersistedValidationService(db, internalValidation, codec, Options.Create(settings ?? new DvsOptions()), NullLogger<PersistedValidationService>.Instance);
    }

    private sealed class FakeInternalValidationService : IDocumentValidationV1Service
    {
        public int Calls { get; private set; }

        public Task<ValidationV1Response> ValidateAsync(ValidationV1Request request, byte[] fileData, string mediaType, string clientId, string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            var fields = new List<ExtractedFieldResponse>
            {
                new() { Field = "expirationDate", Value = "31/12/2030", Confidence = 0.9 },
                new() { Field = "holderName", Value = "T*** S***", Confidence = 0.9, Masked = true }
            };
            if (request.Context.TryGetValue("vehiclePlate", out var plate) && !string.IsNullOrWhiteSpace(plate))
                fields.Add(new ExtractedFieldResponse { Field = "plate", Value = plate, Confidence = 0.9 });

            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ValidationV1Response
            {
                ValidationId = $"VAL-TEST-{Calls}",
                Status = ValidationDecisionStatus.APPROVED,
                Score = 100,
                Confidence = 0.9,
                DocumentType = request.DocumentType,
                DetectedDocumentType = request.DocumentType,
                Policy = request.Policy,
                PolicyVersion = "1.0",
                RulesVersion = $"{request.Policy}:1.0",
                CreatedAt = now,
                CompletedAt = now,
                ProcessingTimeMs = 5,
                CorrelationId = correlationId,
                ExtractedFields = fields
            });
        }
    }
}
