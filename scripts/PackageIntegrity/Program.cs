using System;
using System.Text.Json;
using System.Threading;
using NuGet.Packaging;

// NuGet's locked content hash is the canonical package-content hash. Signing or
// re-signing changes the raw ZIP hash, so hashing the signed .nupkg is insufficient.
// NuGet's own implementation handles the signed ZIP layout and validates its
// integrity before we compare the content hash with locked restored metadata.
try
{
    if (args.Length != 2)
        throw new ArgumentException("Expected: archive.nupkg lockedContentHash");
    using var package = new PackageArchiveReader(args[0]);
    var signature = await package.GetPrimarySignatureAsync(CancellationToken.None);
    if (signature != null)
        await package.ValidateIntegrityAsync(signature.SignatureContent, CancellationToken.None);
    string contentHash = package.GetContentHash(CancellationToken.None);
    if (!string.Equals(contentHash, args[1], StringComparison.Ordinal))
        throw new InvalidOperationException("Canonical NuGet content hash differs from locked metadata.");
    Console.WriteLine(JsonSerializer.Serialize(new { contentHash, signed = signature != null }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Package integrity validation failed: {error.Message}");
    return 1;
}
