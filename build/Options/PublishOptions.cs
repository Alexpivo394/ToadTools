namespace Build.Options;

/// <summary>
///     Release publishing options.
/// </summary>
[Serializable]
public sealed record PublishOptions
{
    /// <summary>
    ///     Path to the changelog file.
    /// </summary>
    public string? ChangelogFile { get; init; }

    /// <summary>
    ///     Private key that signs release files: Base64 PKCS#8 of an ECDSA P-256 key (dotnet run -- keygen).
    ///     CI: the SIGNING_KEY secret as Publish__SigningKey; locally: user-secrets Publish:SigningKey.
    /// </summary>
    public string? SigningKey { get; init; }
}
