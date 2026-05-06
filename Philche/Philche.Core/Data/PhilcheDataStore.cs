using Philche.Core.Data.Migrations;
using Philche.Core.Data.Repositories;

namespace Philche.Core.Data;

public sealed class PhilcheDataStore
{
    public PhilcheDataStore(string databasePath)
    {
        ConnectionFactory = new SqliteConnectionFactory(databasePath);
        MigrationRunner = new SqliteMigrationRunner(ConnectionFactory);

        Inventory = new SqliteInventoryRepository(ConnectionFactory);
        ScanTargets = new SqliteScanTargetRepository(ConnectionFactory);
        Findings = new SqliteFindingRepository(ConnectionFactory);
        ScanRuns = new SqliteScanRunRepository(ConnectionFactory);
        ScanCache = new SqliteScanCacheRepository(ConnectionFactory);
    }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public SqliteMigrationRunner MigrationRunner { get; }

    public IInventoryRepository Inventory { get; }

    public IScanTargetRepository ScanTargets { get; }

    public IFindingRepository Findings { get; }

    public IScanRunRepository ScanRuns { get; }

    public IScanCacheRepository ScanCache { get; }
}
