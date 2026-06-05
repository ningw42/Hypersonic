namespace Hypersonic.Services.Cache;

/// <summary>
/// Metadata the base computes after a streamed payload write completes successfully.
/// Passed to <c>WriteStreamedPayloadAsync</c>'s <c>entryFactory</c> so the subclass
/// can stamp the actual written length and access timestamp onto its <c>TEntry</c>.
/// The factory is responsible for setting <c>FileName</c> on the returned entry —
/// the base uses that final name as the rename target for the temp file.
/// </summary>
public sealed record PayloadWriteContext(long Length, DateTimeOffset WrittenAtUtc);
