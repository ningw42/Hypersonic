namespace Hypersonic.Services.Cache;

/// <summary>
/// One incremental schema bump declared by a subclass. The base's migration loop
/// runs every step whose <see cref="TargetVersion"/> is greater than the current
/// <c>PRAGMA user_version</c>, in order, inside a single outer transaction.
/// Any failure rolls the entire bump back and leaves <c>user_version</c> unchanged
/// so the next launch retries cleanly.
/// </summary>
public sealed record MigrationStep(int TargetVersion, string Sql);
