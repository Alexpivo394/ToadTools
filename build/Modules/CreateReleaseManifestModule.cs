using System.Text.Json;
using System.Text.Json.Nodes;
using Build.Options;
using Microsoft.Extensions.Options;
using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.GitHub.Extensions;
using ModularPipelines.Modules;

namespace Build.Modules;

/// <summary>
///     output/latest.json — описание релиза для программы ToadTools в том же виде, что ответ API GitHub о релизе
///     (tag_name, body, assets). Программа читает его по постоянной ссылке releases/latest/download/latest.json: её
///     отдаёт CDN GitHub без лимита API (60 запросов в час на адрес без токена — мало для офиса за одним роутером).
/// </summary>
[DependsOn<ResolveVersioningModule>]
[DependsOn<GenerateChangelogModule>]
[DependsOn<SignArtifactsModule>]
public sealed class CreateReleaseManifestModule(IOptions<BuildOptions> buildOptions) : Module
{
    public const string FileName = "latest.json";

    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var versioning = (await context.GetModule<ResolveVersioningModule>()).ValueOrDefault!;
        var notes = (await context.GetModule<GenerateChangelogModule>()).ValueOrDefault ?? "";
        // Owner/Name; у remote вида https://github.com/owner/name.git имя приходит с ".git" — в ссылках его быть не должно
        var repository = context.GitHub().RepositoryInfo.Identifier;
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repository = repository[..^4];
        var outputFolder = context.Git().RootDirectory.GetFolder(buildOptions.Value.OutputDirectory);

        var assets = new JsonArray();
        foreach (var file in Directory.GetFiles(outputFolder.Path).Where(file => Path.GetFileName(file) != FileName).Order())
        {
            var name = Path.GetFileName(file);
            assets.Add(new JsonObject
            {
                ["name"] = name,
                ["size"] = new FileInfo(file).Length,
                ["browser_download_url"] = $"https://github.com/{repository}/releases/download/{versioning.Version}/{name}"
            });
        }

        var manifest = new JsonObject
        {
            ["tag_name"] = versioning.Version,
            ["name"] = versioning.Version,
            ["body"] = notes.Trim(),
            ["assets"] = assets
        };

        var path = Path.Combine(outputFolder.Path, FileName);
        await File.WriteAllTextAsync(path, manifest.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }), cancellationToken);
        context.Summary.KeyValue("Artifacts", "Release manifest", path);
    }
}
