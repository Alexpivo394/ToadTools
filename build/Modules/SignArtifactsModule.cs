using Build.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;

namespace Build.Modules;

/// <summary>
///     Подписи релиза: рядом с каждым .zip и .msi в output кладётся {файл}.sig (ECDSA P-256 над SHA-256, ключ —
///     Publish:SigningKey, в CI секрет SIGNING_KEY). Программа ToadTools не ставит файлы без верной подписи.
///     Без ключа подписи не создаются, и публикация (PublishGithubModule) откажется выкладывать релиз.
/// </summary>
[DependsOn<CreatePluginArchiveModule>(Optional = true)]
public sealed class SignArtifactsModule(IOptions<BuildOptions> buildOptions, IOptions<PublishOptions> publishOptions) : Module
{
    protected override Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var key = publishOptions.Value.SigningKey;
        var outputFolder = context.Git().RootDirectory.GetFolder(buildOptions.Value.OutputDirectory);
        if (string.IsNullOrWhiteSpace(key))
        {
            context.Logger.LogWarning("Publish:SigningKey is not set: release files are not signed and cannot be published");
            return Task.CompletedTask;
        }

        foreach (var file in Directory.GetFiles(outputFolder.Path).Where(IsSigned))
        {
            File.WriteAllText(file + ".sig", SigningKeys.Sign(key, file));
            context.Summary.KeyValue("Artifacts", "Signature", file + ".sig");
        }

        return Task.CompletedTask;
    }

    /// <summary>Файлы релиза, которые программа проверяет подписью.</summary>
    public static bool IsSigned(string file) =>
        file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
}
