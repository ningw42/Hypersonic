namespace Hypersonic.Services.Cache;

/// <summary>
/// One declarative payload-folder rename declared by a subclass, symmetric with
/// <see cref="MigrationStep"/> for schema. The driver joins
/// <see cref="SourceSubfolder"/> and <see cref="TargetSubfolder"/> with the cache
/// parent (<c>IAppFolderLocator.GetCacheFolder()</c>) internally, so subclasses
/// declare leaf names only and never compute the parent path themselves.
///
/// <para>
/// The driver runs every step whose <see cref="TargetVersion"/> is greater than the
/// current per-store marker version, in order. Each step is a single
/// <c>Directory.Move</c> (or a no-op) followed by a marker bump.
/// </para>
/// </summary>
public sealed record PayloadFolderMigrationStep(
    int TargetVersion,
    string SourceSubfolder,
    string TargetSubfolder);
