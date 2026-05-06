namespace Philche.Core.Data.Migrations;

public static class MigrationCatalog
{
    public static IReadOnlyList<MigrationScript> All { get; } =
    [
        new(
            "001_init",
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id TEXT NOT NULL PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS inventory_items (
                id TEXT NOT NULL PRIMARY KEY,
                agent_key TEXT NOT NULL,
                surface_type TEXT NOT NULL,
                surface_target_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                version TEXT NOT NULL,
                executable_path TEXT NULL,
                version_evidence TEXT NULL,
                discovered_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_identity
            ON inventory_items(agent_key, surface_type, surface_target_id, version);

            CREATE TABLE IF NOT EXISTS scan_targets (
                id TEXT NOT NULL PRIMARY KEY,
                surface_type TEXT NOT NULL,
                target_key TEXT NOT NULL,
                display_name TEXT NOT NULL,
                is_selected INTEGER NOT NULL,
                is_newly_discovered INTEGER NOT NULL,
                discovered_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_scan_targets_identity
            ON scan_targets(surface_type, target_key);

            CREATE TABLE IF NOT EXISTS findings (
                id TEXT NOT NULL PRIMARY KEY,
                canonical_vulnerability_id TEXT NOT NULL,
                target_id TEXT NOT NULL,
                finding_type TEXT NOT NULL,
                summary TEXT NULL,
                description TEXT NULL,
                severity TEXT NULL,
                skills_risk_level TEXT NULL,
                provenance_json TEXT NOT NULL,
                source_references TEXT NULL,
                first_seen_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY(target_id) REFERENCES scan_targets(id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_findings_identity
            ON findings(canonical_vulnerability_id, target_id, finding_type);

            CREATE TABLE IF NOT EXISTS scan_runs (
                id TEXT NOT NULL PRIMARY KEY,
                trigger_reason TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                ended_at_utc TEXT NULL,
                inventory_count INTEGER NOT NULL,
                finding_count INTEGER NOT NULL,
                warning_count INTEGER NOT NULL,
                status TEXT NOT NULL
            );
            """),
        new(
            "002_add_finding_lines",
            """
            ALTER TABLE findings ADD COLUMN start_line INTEGER NULL;
            ALTER TABLE findings ADD COLUMN end_line INTEGER NULL;
            """),
        new(
            "003_add_finding_summaries",
            """
            ALTER TABLE findings ADD COLUMN original_summary TEXT NULL;
            ALTER TABLE findings ADD COLUMN simplified_summary TEXT NULL;
            """),
        new(
            "004_add_scan_cache",
            """
            CREATE TABLE IF NOT EXISTS scan_cache (
                skill_path TEXT NOT NULL,
                scan_type TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                last_scanned_utc TEXT NOT NULL,
                findings_json TEXT NOT NULL,
                PRIMARY KEY(skill_path, scan_type)
            );

            CREATE INDEX IF NOT EXISTS ix_scan_cache_scan_type
            ON scan_cache(scan_type);
            """),
        new(
            "005_add_cache_agent_version",
            """
            ALTER TABLE scan_cache ADD COLUMN agent_version TEXT NULL;
            DELETE FROM scan_cache;
            """),
        new(
            "006_add_scan_run_risky_paths",
            """
            ALTER TABLE scan_runs ADD COLUMN high_risk_paths_json TEXT NULL;
            """)
    ];
}
