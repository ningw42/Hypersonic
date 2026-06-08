namespace Hypersonic.Messages;

/// <summary>
/// Broadcast by <c>FavoritesService</c> whenever the starred state of one or more
/// songs changes — from a user toggle, the background <c>getStarred2</c> poll, or a
/// page-load reconcile. A <em>delta</em>, not a snapshot: <see cref="Ids"/> are all the
/// songs that just moved to <see cref="IsStarred"/>. Batching by the shared boolean lets
/// a poll cycle emit at most two messages (one newly-starred group, one newly-unstarred).
/// Subscribers update only the model instances they own whose id is in <see cref="Ids"/>.
/// </summary>
public sealed record FavoritesChangedMessage(bool IsStarred, IReadOnlyList<string> Ids);
