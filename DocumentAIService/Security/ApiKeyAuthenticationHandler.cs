using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using DocumentAIService.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DocumentAIService.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<DvsOptions> dvsOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var supplied) || string.IsNullOrWhiteSpace(supplied))
            return Task.FromResult(AuthenticateResult.NoResult());

        var candidate = supplied.ToString();
        var configuredKey = dvsOptions.Value.Security.ApiKeys.FirstOrDefault(x =>
            x.Active && FixedTimeEquals(x.Key, candidate));

        if (configuredKey is null)
            return Task.FromResult(AuthenticateResult.Fail("Chave de API inválida."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, configuredKey.Id),
            new Claim(ClaimTypes.Name, configuredKey.Id),
            new Claim(ClaimTypes.Role, configuredKey.Role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private static bool FixedTimeEquals(string configured, string supplied)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(configured ?? string.Empty);
        var b = System.Text.Encoding.UTF8.GetBytes(supplied ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
