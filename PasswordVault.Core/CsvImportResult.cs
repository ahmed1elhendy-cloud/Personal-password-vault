namespace PasswordVault.Core;

public sealed record CsvImportResult(
    List<VaultEntry> Entries,
    List<Category> NewCategories,
    int SkippedRows,
    int DuplicateRows);
