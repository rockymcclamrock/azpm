namespace Azpm;

/// <summary>Turns the <c>--service-principal</c> family of CLI flags into a <see cref="ServicePrincipal"/>.</summary>
public static class ServicePrincipalInput
{
    public static ServicePrincipal? Resolve(
        bool servicePrincipal,
        string? clientId,
        string? tenant,
        string? clientSecret,
        bool clientSecretStdin,
        string? certificatePath,
        TextReader stdin)
    {
        var anyFlag = servicePrincipal || clientId is not null || clientSecret is not null
                      || clientSecretStdin || certificatePath is not null;
        if (!anyFlag)
            return null;

        if (string.IsNullOrWhiteSpace(clientId))
            throw new AzpmException(ExitCode.UsageError, "a service principal needs --client-id");
        if (string.IsNullOrWhiteSpace(tenant))
            throw new AzpmException(ExitCode.UsageError, "a service principal needs --tenant");

        var secret = clientSecretStdin ? stdin.ReadLine()?.Trim() : clientSecret;
        var haveSecret = !string.IsNullOrEmpty(secret);
        var haveCert = !string.IsNullOrWhiteSpace(certificatePath);

        if (haveSecret == haveCert)
            throw new AzpmException(ExitCode.UsageError,
                "a service principal needs exactly one of --client-secret, --client-secret-stdin, --certificate");

        if (haveCert && !File.Exists(certificatePath))
            throw new AzpmException(ExitCode.UsageError, $"certificate file not found: {certificatePath}");

        return new ServicePrincipal
        {
            ClientId = clientId,
            TenantId = tenant,
            Auth = haveCert ? "certificate" : "secret",
            Secret = haveCert ? null : secret,
            CertificatePath = haveCert ? Path.GetFullPath(certificatePath!) : null,
        };
    }
}
