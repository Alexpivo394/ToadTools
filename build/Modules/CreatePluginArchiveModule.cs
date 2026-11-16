using System.IO.Compression;
using System.Text.RegularExpressions;
using Build.Options;
using Microsoft.Extensions.Options;
using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;
using Shouldly;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Архив плагина для программы ToadTools: output/ToadTools-{версия}.zip, внутри {год}/ToadTools.addin и
///     {год}/ToadTools/** — содержимое source/ToadTools/bin/Release.R{yy}/publish.
/// </summary>
[DependsOn<ResolveVersioningModule>]
[DependsOn<CompileProjectModule>]
public sealed partial class CreatePluginArchiveModule(IOptions<BuildOptions> buildOptions) : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var versioning = (await context.GetModule<ResolveVersioningModule>()).ValueOrDefault!;
        var binFolder = Path.Combine(Path.GetDirectoryName(Projects.ToadTools.FullName)!, "bin");
        var publishFolders = Directory.GetDirectories(binFolder)
            .Select(folder => (Match: ConfigurationYear().Match(Path.GetFileName(folder)), Publish: Path.Combine(folder, "publish")))
            .Where(item => item.Match.Success && Directory.Exists(item.Publish))
            .Select(item => (Year: 2000 + int.Parse(item.Match.Groups[1].Value), item.Publish))
            .OrderBy(item => item.Year)
            .ToArray();
        publishFolders.ShouldNotBeEmpty("No Release.Rxx/publish folders of the add-in were found to pack");

        var outputFolder = context.Git().RootDirectory.GetFolder(buildOptions.Value.OutputDirectory);
        Directory.CreateDirectory(outputFolder.Path);
        var archive = Path.Combine(outputFolder.Path, $"ToadTools-{versioning.Version}.zip");
        if (File.Exists(archive)) File.Delete(archive);

        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (var (year, publish) in publishFolders)
            {
                File.Exists(Path.Combine(publish, "ToadTools.addin")).ShouldBeTrue($"{publish} has no ToadTools.addin");
                foreach (var file in Directory.GetFiles(publish, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(publish, file).Replace(Path.DirectorySeparatorChar, '/');
                    zip.CreateEntryFromFile(file, $"{year}/{relative}", CompressionLevel.Optimal);
                }
            }
        }

        context.Summary.KeyValue("Artifacts", "Plugin archive", archive);
    }

    [GeneratedRegex(@"^Release\.R(\d{2})$")]
    private static partial Regex ConfigurationYear();
}
