using Build.Modules;
using Build.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines;
using ModularPipelines.Extensions;

if (args.Contains("keygen"))
{
    Build.SigningKeys.Generate(args.SkipWhile(arg => arg != "keygen").Skip(1).FirstOrDefault() ?? "signing-keys.txt");
    return;
}

// dotnet run -- sign <file>: {file}.sig with the key from the SIGNING_KEY environment variable (manual checks of the program)
if (args.Contains("sign"))
{
    var file = args.SkipWhile(arg => arg != "sign").Skip(1).First();
    File.WriteAllText(file + ".sig", Build.SigningKeys.Sign(Environment.GetEnvironmentVariable("SIGNING_KEY")!, file));
    return;
}

var builder = Pipeline.CreateBuilder();

builder.Configuration.AddJsonFile("appsettings.json");
builder.Configuration.AddUserSecrets<Program>();
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOptions<BuildOptions>().Bind(builder.Configuration.GetSection("Build"));
builder.Services.AddOptions<PublishOptions>().Bind(builder.Configuration.GetSection("Publish"));

if (args.Length == 0)
{
    builder.Services.AddModule<CompileProjectModule>();
}

if (args.Contains("test"))
{
    builder.Services.AddModule<TestProjectModule>();
}

if (args.Contains("pack"))
{
    builder.Services.AddModule<CleanProjectModule>();
    builder.Services.AddModule<CreatePluginArchiveModule>();
    builder.Services.AddModule<SignArtifactsModule>();
    builder.Services.AddModule<CreateReleaseManifestModule>();
}

if (args.Contains("publish"))
{
    builder.Services.AddModule<PublishGithubModule>();
}

await builder.Build().RunAsync();