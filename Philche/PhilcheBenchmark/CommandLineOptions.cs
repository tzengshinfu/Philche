namespace PhilcheBenchmark;

internal sealed record CommandLineOptions
{
    public const string Usage = "Usage: PhilcheBenchmark --model <path-to-gguf> [--cases <path-to-test-cases>] [--help]";

    public string? ModelPath { get; private init; }

    public string? CasesPath { get; private init; }

    public bool ShowHelp { get; private init; }

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    options = options with { ShowHelp = true };
                    break;
                case "--model":
                    index = RequireValue(args, index, "--model");
                    options = options with { ModelPath = args[index] };
                    break;
                case "--cases":
                    index = RequireValue(args, index, "--cases");
                    options = options with { CasesPath = args[index] };
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        return options;
    }

    private static int RequireValue(string[] args, int index, string option)
    {
        var valueIndex = index + 1;
        if (valueIndex >= args.Length || args[valueIndex].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return valueIndex;
    }
}
