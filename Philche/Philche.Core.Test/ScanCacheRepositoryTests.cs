using Philche.Core.Data;
using Philche.Core.Domain.Models;

namespace Philche.Core.Test;

public sealed class ScanCacheRepositoryTests
{
    [Fact(DisplayName = "掃描快取存放庫測試：Upsert And Get Async Round Trips Entry")]
    public async Task UpsertAndGetAsync_RoundTripsEntry()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-cache-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            var entry = new ScanCacheEntry
            {
                SkillPath = "demo-skill",
                ScanType = "prompt",
                ContentHash = "abc123",
                AgentVersion = null,
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[{\"risk\":\"high\"}]",
            };

            await store.ScanCache.UpsertAsync(entry);

            var loaded = await store.ScanCache.GetAsync("demo-skill", "prompt");
            Assert.NotNull(loaded);
            Assert.Equal(entry.SkillPath, loaded.SkillPath);
            Assert.Equal(entry.ScanType, loaded.ScanType);
            Assert.Equal(entry.ContentHash, loaded.ContentHash);
            Assert.Equal(entry.FindingsJson, loaded.FindingsJson);
            Assert.Null(loaded.AgentVersion);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact(DisplayName = "掃描快取存放庫測試：Upsert Async On Same Key Updates Entry")]
    public async Task UpsertAsync_OnSameKey_UpdatesEntry()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-cache-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "demo-skill",
                ScanType = "code",
                ContentHash = "hash-v1",
                AgentVersion = null,
                LastScannedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                FindingsJson = "[]",
            });

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "demo-skill",
                ScanType = "code",
                ContentHash = "hash-v2",
                AgentVersion = null,
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[{\"risk\":\"medium\"}]",
            });

            var loaded = await store.ScanCache.GetAsync("demo-skill", "code");
            Assert.NotNull(loaded);
            Assert.Equal("hash-v2", loaded.ContentHash);
            Assert.Equal("[{\"risk\":\"medium\"}]", loaded.FindingsJson);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact(DisplayName = "掃描快取存放庫測試：Delete By Scan Type Async Removes Only Requested Type")]
    public async Task DeleteByScanTypeAsync_RemovesOnlyRequestedType()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-cache-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "a",
                ScanType = "prompt",
                ContentHash = "p1",
                AgentVersion = null,
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[]",
            });

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "a",
                ScanType = "code",
                ContentHash = "c1",
                AgentVersion = null,
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[]",
            });

            await store.ScanCache.DeleteByScanTypeAsync("code");

            Assert.Null(await store.ScanCache.GetAsync("a", "code"));
            Assert.NotNull(await store.ScanCache.GetAsync("a", "prompt"));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact(DisplayName = "掃描快取存放庫測試：Clear All Async Removes All Entries")]
    public async Task ClearAllAsync_RemovesAllEntries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-cache-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "a",
                ScanType = "prompt",
                ContentHash = "p1",
                AgentVersion = null,
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[]",
            });
            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "b",
                ScanType = "code",
                ContentHash = "c1",
                AgentVersion = null,
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[]",
            });

            await store.ScanCache.ClearAllAsync();

            Assert.Null(await store.ScanCache.GetAsync("a", "prompt"));
            Assert.Null(await store.ScanCache.GetAsync("b", "code"));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact(DisplayName = "掃描快取存放庫測試：Upsert And Get Async Round Trips Agent Version")]
    public async Task UpsertAndGetAsync_RoundTripsAgentVersion()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"philche-cache-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PhilcheDataStore(dbPath);
            await store.MigrationRunner.ApplyAsync();

            await store.ScanCache.UpsertAsync(new ScanCacheEntry
            {
                SkillPath = "openclaw",
                ScanType = ScanCacheTypes.Posture,
                ContentHash = string.Empty,
                AgentVersion = "1.2.3",
                LastScannedAt = DateTimeOffset.UtcNow,
                FindingsJson = "[{\"ruleId\":\"openclaw-cli-audit\"}]",
            });

            var loaded = await store.ScanCache.GetAsync("openclaw", ScanCacheTypes.Posture);
            Assert.NotNull(loaded);
            Assert.Equal("1.2.3", loaded!.AgentVersion);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}


