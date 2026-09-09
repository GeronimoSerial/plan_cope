using PlanCope.RosterCrypto;

if (args.Length == 0 || !string.Equals(args[0], "pack", StringComparison.OrdinalIgnoreCase))
{
    return Usage("Expected command 'pack'.");
}

var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var index = 1; index < args.Length; index += 2)
{
    if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
        return Usage($"Invalid argument '{args[index]}'.");
    options[args[index][2..]] = args[index + 1];
}
if (!options.TryGetValue("input", out var input) || !options.TryGetValue("output", out var output) ||
    !options.TryGetValue("passphrase", out var passphrase))
    return Usage("--input, --output and --passphrase are required.");

try
{
    var parameters = new Argon2Parameters(
        ParsePositive(options, "memory-kib", 19_456),
        ParsePositive(options, "iterations", 2),
        ParsePositive(options, "parallelism", 1));
    var manifest = await EnvelopeEncryption.EncryptDirectoryAsync(input, output, passphrase, parameters);
    var manifestPath = $"{output}.manifest.json";
    await EnvelopeEncryption.WriteManifestAsync(manifest, manifestPath);
    Console.WriteLine($"Packed {manifest.Entries.Count} synthetic-compatible roster entries into '{output}'.");
    Console.WriteLine($"Manifest: '{manifestPath}'.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int ParsePositive(IReadOnlyDictionary<string, string> options, string name, int fallback) =>
    options.TryGetValue(name, out var raw) && int.TryParse(raw, out var value) && value > 0
        ? value
        : options.ContainsKey(name) ? throw new ArgumentException($"--{name} must be a positive integer.") : fallback;

static int Usage(string error)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine("Usage: pack --input <directory> --output <bundle.enc> --passphrase <value> [--memory-kib N --iterations N --parallelism N]");
    return 2;
}
