namespace PasswordVault.Core;

public sealed record KdfParams(int MemoryKb, int Iterations, int Parallelism)
{
    public static KdfParams Default { get; } = new(65536, 3, 2);
}
