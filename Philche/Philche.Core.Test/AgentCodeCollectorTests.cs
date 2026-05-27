using Philche.Core.SkillsRisk;

namespace Philche.Core.Test;

public sealed class AgentCodeCollectorTests
{
    [Fact(DisplayName = "代理程式碼收集測試：Scan Directories Async Skips Non Existent Directory")]
    public async Task ScanDirectoriesAsync_SkipsNonExistentDirectory()
    {
        var collector = new AgentCodeCollector();

        var result = await collector.ScanDirectoriesAsync(["/nonexistent/path/does/not/exist"]);

        Assert.Equal(0, result.TotalFilesScanned);
        Assert.Equal(0, result.FilesWithFindings);
        Assert.Empty(result.FileResults);
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Directories Async Filters Extensions")]
    public async Task ScanDirectoriesAsync_FiltersExtensions()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "script.py"), "import os; os.system('cmd.exe /c calc')");
            File.WriteAllText(Path.Combine(tempDir, "image.png"), "not code");
            File.WriteAllText(Path.Combine(tempDir, "readme.md"), "just a readme");

            var collector = new AgentCodeCollector();
            var result = await collector.ScanDirectoriesAsync([tempDir]);

            Assert.Equal(2, result.TotalFilesScanned);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Directories Async Skips Blacklisted Directories")]
    public async Task ScanDirectoriesAsync_SkipsBlacklistedDirectories()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var nodeModules = Path.Combine(tempDir, "node_modules");
            Directory.CreateDirectory(nodeModules);
            File.WriteAllText(Path.Combine(nodeModules, "dep.js"), "process.start('cmd.exe')");

            var src = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.py"), "print('hello')");

            var collector = new AgentCodeCollector();
            var result = await collector.ScanDirectoriesAsync([tempDir]);

            Assert.Equal(1, result.TotalFilesScanned);
            Assert.All(result.FileResults, f => Assert.DoesNotContain("node_modules", f.FilePath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Directories Async Detects Findings")]
    public async Task ScanDirectoriesAsync_DetectsFindings()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "evil.py"), "import subprocess\nsubprocess.Popen('cmd.exe')");
            File.WriteAllText(Path.Combine(tempDir, "safe.py"), "print('hello world')");

            var collector = new AgentCodeCollector();
            var result = await collector.ScanDirectoriesAsync([tempDir]);

            Assert.Equal(2, result.TotalFilesScanned);
            Assert.True(result.FilesWithFindings >= 1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Directories Async Skips Oversized Files")]
    public async Task ScanDirectoriesAsync_SkipsOversizedFiles()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bigFile = Path.Combine(tempDir, "huge.py");
            File.WriteAllText(bigFile, new string('x', 2 * 1024 * 1024));

            var smallFile = Path.Combine(tempDir, "small.py");
            File.WriteAllText(smallFile, "print('hello')");

            var collector = new AgentCodeCollector();
            var result = await collector.ScanDirectoriesAsync([tempDir]);

            Assert.Equal(1, result.TotalFilesScanned);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Directories Async Cancellation Is Respected")]
    public async Task ScanDirectoriesAsync_CancellationIsRespected()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"file{i}.py"), "print('test')");
            }

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var collector = new AgentCodeCollector();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => collector.ScanDirectoriesAsync([tempDir], cts.Token));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Files Async Scans Specific Files Only")]
    public async Task ScanFilesAsync_ScansSpecificFilesOnly()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var risky = Path.Combine(tempDir, "risky.py");
            var safe = Path.Combine(tempDir, "safe.py");
            File.WriteAllText(risky, "subprocess.Popen('cmd.exe')");
            File.WriteAllText(safe, "print('safe')");

            var collector = new AgentCodeCollector();
            var result = await collector.ScanFilesAsync([risky, safe]);

            Assert.Equal(2, result.TotalFilesScanned);
            Assert.True(result.FilesWithFindings >= 1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Files Async Skips Deleted File")]
    public async Task ScanFilesAsync_SkipsDeletedFile()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDir, "gone.py");
            File.WriteAllText(filePath, "print('hello')");
            File.Delete(filePath);

            var collector = new AgentCodeCollector();
            var result = await collector.ScanFilesAsync([filePath]);

            Assert.Equal(0, result.TotalFilesScanned);
            Assert.Equal(0, result.FilesWithFindings);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Files Async Skips Oversized File")]
    public async Task ScanFilesAsync_SkipsOversizedFile()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bigFile = Path.Combine(tempDir, "huge.py");
            File.WriteAllText(bigFile, new string('x', 2 * 1024 * 1024));

            var collector = new AgentCodeCollector();
            var result = await collector.ScanFilesAsync([bigFile]);

            Assert.Equal(0, result.TotalFilesScanned);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Scan Files Async Skips Non Whitelisted Extension")]
    public async Task ScanFilesAsync_SkipsNonWhitelistedExtension()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDir, "note.txt");
            File.WriteAllText(filePath, "cmd.exe /c calc");

            var collector = new AgentCodeCollector();
            var result = await collector.ScanFilesAsync([filePath]);

            Assert.Equal(0, result.TotalFilesScanned);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Compute Single File Hash Async Changes When File Content Changes")]
    public async Task ComputeSingleFileHashAsync_ChangesWhenFileContentChanges()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDir, "skill.py");
            await File.WriteAllTextAsync(filePath, "print('safe')");

            var before = await AgentCodeCollector.ComputeSingleFileHashAsync(filePath);

            await File.WriteAllTextAsync(filePath, "import subprocess\nsubprocess.Popen('cmd.exe')");
            var after = await AgentCodeCollector.ComputeSingleFileHashAsync(filePath);

            Assert.NotEqual(before, after);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "代理程式碼收集測試：Compute Single File Hash Async For Missing File Returns Deterministic Marker Hash")]
    public async Task ComputeSingleFileHashAsync_ForMissingFile_ReturnsDeterministicMarkerHash()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var missingPath = Path.Combine(tempDir, "missing.py");

            var first = await AgentCodeCollector.ComputeSingleFileHashAsync(missingPath);
            var second = await AgentCodeCollector.ComputeSingleFileHashAsync(missingPath);

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"philche-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}


