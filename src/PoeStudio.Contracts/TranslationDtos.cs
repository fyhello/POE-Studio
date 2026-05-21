namespace PoeStudio.Contracts;

public sealed record TranslationExportRequest(
    string ProfileId,
    string Query,
    int Take = 200);

public sealed record TranslationImportRequest(
    string ProfileId,
    string Csv);

public sealed record TranslationApplyGlossaryRequest(
    string ProfileId,
    string Csv,
    string Glossary);

public sealed record TranslationEntryDto(
    string VirtualPath,
    string SourceText,
    string TargetText,
    string Status);

public sealed record TranslationExportResponse(
    string ProfileId,
    int Matched,
    int Exported,
    string Csv,
    IReadOnlyList<string> Warnings);

public sealed record TranslationImportResponse(
    string ProfileId,
    int Imported,
    int Applied,
    IReadOnlyList<string> AppliedPaths,
    IReadOnlyList<string> Warnings);

public sealed record TranslationApplyGlossaryResponse(
    string ProfileId,
    int Entries,
    int Terms,
    int Changed,
    string Csv,
    IReadOnlyList<string> Warnings);
