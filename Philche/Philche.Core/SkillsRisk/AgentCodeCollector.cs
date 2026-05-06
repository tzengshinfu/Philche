namespace Philche.Core.SkillsRisk;

using System.Security.Cryptography;
using System.Text;

public sealed class AgentCodeCollector
{
    private readonly YaraCodeScanner scanner;

    private static readonly HashSet<string> DefaultCodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".py", ".js", ".ts", ".jsx", ".tsx",
        ".sh", ".bash", ".ps1", ".psm1",
        ".cs", ".java", ".go", ".rb", ".rs",
        ".yaml", ".yml", ".json", ".toml", ".md",
    };

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "__pycache__",
        ".venv", "venv", ".tox", "dist", "build",
    };

    private const long MaxFileSizeBytes = 1 * 1024 * 1024;

    private readonly HashSet<string> codeExtensions;

    public AgentCodeCollector(YaraCodeScanner? scanner = null, IEnumerable<string>? customExtensions = null)
    {
        this.scanner = scanner ?? new YaraCodeScanner();
        codeExtensions = customExtensions is not null
            ? new HashSet<string>(customExtensions, StringComparer.OrdinalIgnoreCase)
            : DefaultCodeExtensions;
    }

    public async Task<AgentCodeScanResult> ScanDirectoriesAsync(
        IEnumerable<string> rootPaths,
        CancellationToken cancellationToken = default)
    {
        var fileResults = new List<CodeScanFileResult>();
        var totalFiles = 0;
        var filesWithFindings = 0;

        foreach (var rootPath in rootPaths)
        {
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var filePath in EnumerateCodeFiles(rootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var evidence = await ScanFileAsync(filePath, cancellationToken);
                totalFiles++;

                if (evidence.Count > 0)
                {
                    filesWithFindings++;
                    fileResults.Add(new CodeScanFileResult(filePath, evidence));
                }
            }
        }

        return new AgentCodeScanResult(fileResults, totalFiles, filesWithFindings);
    }

    public IReadOnlyList<string> GetScannableFiles(IEnumerable<string> rootPaths)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in rootPaths)
        {
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var filePath in EnumerateCodeFiles(rootPath))
            {
                files.Add(filePath);
            }
        }

        return files.ToList();
    }

    public async Task<AgentCodeScanResult> ScanFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var fileResults = new List<CodeScanFileResult>();
        var totalFiles = 0;
        var filesWithFindings = 0;

        foreach (var filePath in filePaths
                     .Where(static p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsScannableFile(filePath))
            {
                continue;
            }

            var evidence = await ScanFileAsync(filePath, cancellationToken);
            totalFiles++;

            if (evidence.Count > 0)
            {
                filesWithFindings++;
                fileResults.Add(new CodeScanFileResult(filePath, evidence));
            }
        }

        return new AgentCodeScanResult(fileResults, totalFiles, filesWithFindings);
    }

    public bool IsScannableFilePath(string filePath)
    {
        return IsScannableFile(filePath);
    }

    public static async Task<string> ComputeSingleFileHashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        using var sha = SHA256.Create();

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(content)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes("<inaccessible>")));
        }
    }

    public async Task<string> ComputeCodeContentHashAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var normalizedFiles = filePaths
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => IsScannableFile(path))
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var filePath in normalizedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();
            sha.AppendData(Encoding.UTF8.GetBytes(normalizedPath));
            sha.AppendData([0]);

            try
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                sha.AppendData(Encoding.UTF8.GetBytes(content));
                sha.AppendData([0]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep hash stable for inaccessible files by adding an explicit marker.
                sha.AppendData(Encoding.UTF8.GetBytes("<inaccessible>"));
                sha.AppendData([0]);
            }
        }

        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private IEnumerable<string> EnumerateCodeFiles(string rootPath)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden,
        };

        return Directory.EnumerateFiles(rootPath, "*", options)
            .Where(filePath =>
            {
                return IsScannableFile(filePath, rootPath);
            });
    }

    private bool IsScannableFile(string filePath, string? rootPath = null)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        if (!codeExtensions.Contains(ext))
        {
            return false;
        }

        var relativePath = rootPath is null
            ? filePath
            : Path.GetRelativePath(rootPath, filePath);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(SkipDirectories.Contains))
        {
            return false;
        }

        try
        {
            return new FileInfo(filePath).Length <= MaxFileSizeBytes;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<SkillRiskEvidence>> ScanFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return scanner.Scan(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
