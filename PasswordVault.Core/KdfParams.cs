namespace PasswordVault.Core;

public sealed record KdfParams
{
    public const int MaximumMemoryKb = 262144;
    public const int MaximumIterations = 10;
    public const int MaximumParallelism = 16;

    public KdfParams(int MemoryKb, int Iterations, int Parallelism)
    {
        if (Parallelism is < 1 or > MaximumParallelism)
        {
            throw new ArgumentOutOfRangeException(nameof(Parallelism));
        }

        if (MemoryKb < 8 * Parallelism || MemoryKb > MaximumMemoryKb)
        {
            throw new ArgumentOutOfRangeException(nameof(MemoryKb));
        }

        if (Iterations is < 1 or > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(Iterations));
        }

        this.MemoryKb = MemoryKb;
        this.Iterations = Iterations;
        this.Parallelism = Parallelism;
    }

    public int MemoryKb { get; }
    public int Iterations { get; }
    public int Parallelism { get; }

    public static KdfParams Default { get; } = new(65536, 3, 2);
}
