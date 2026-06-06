namespace Hypersonic.Services.Cache;

/// <summary>
/// Per-store folder-migration marker persisted as <c>&lt;entity&gt;.json</c> at the
/// cache parent (next to the entity folder, so a folder rename does not lose it).
/// Symmetric with <c>PRAGMA user_version</c> for schema migration.
///
/// <para>
/// Minimal and extensible: future per-store metadata (last-migrated timestamp,
/// telemetry) can be added as new fields without a format change. A missing,
/// unreadable, unparseable, or field-less marker degrades to
/// <see cref="FolderVersion"/> <c>0</c> — the safe "haven't migrated yet" default.
/// </para>
/// </summary>
public sealed record PayloadFolderMetadata(int FolderVersion);
