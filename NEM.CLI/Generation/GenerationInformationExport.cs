using NEM.Contracts;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Generation;

internal static class GenerationInformationExport
{
    public static GenerationInformationDTO Create(
        string sourcePath,
        IReadOnlyList<GenerationInformationRow> rows)
    {
        return new GenerationInformationDTO(
            ArtifactSchemaVersions.GenerationInformation,
            Path.GetFileName(sourcePath),
            DateTimeOffset.UtcNow,
            rows.ToArray());
    }

    public static void WriteJson(GenerationInformationDTO data, string path)
        => JsonFile.Write(data, path);
}