using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace DocumentAIService.Security;

public interface IProtectedDataCodec
{
    string? Protect(string? value);
    string? Unprotect(string? value);
}

/// <summary>
/// Criptografa dados pessoais persistidos. Respostas HTTP continuam mascaradas pelo minimizador;
/// dados decifrados não devem ser registrados em logs.
/// </summary>
public sealed class ProtectedDataCodec(IDataProtectionProvider provider) : IProtectedDataCodec
{
    private readonly IDataProtector _protector = provider.CreateProtector("DocumentAIService.Compliance.Persistence.v1");

    public string? Protect(string? value) => string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value.Trim());

    public string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return _protector.Unprotect(value); }
        catch (CryptographicException) { return null; }
    }
}
