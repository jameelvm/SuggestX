namespace SuggestX.SuggestionService.Services;

/// <summary>
/// The in-memory cache of "which trie version is current," refreshed by
/// <c>CurrentVersionPoller</c> on a timer. <c>SuggestionsController</c>
/// reads this synchronously on every request — no per-request ZooKeeper
/// round trip, the same reasoning as the Redis <c>GET</c> itself: decision
/// 2 means nothing on this read path pays a network hop it doesn't have to.
/// Null until the first successful poll (or forever, if TrieBuilder has
/// never published anything) — a real, distinguishable "don't know yet"
/// state, not defaulted to some fake version number.
/// </summary>
public interface ICurrentTrieVersion
{
    int? Version { get; }
    void Set(int version);
}

public sealed class CurrentTrieVersionHolder : ICurrentTrieVersion
{
    private const int Unset = -1;

    // volatile so a read on one thread promptly sees a write made by the
    // poller on another — same reasoning as TrieBuilder's own ITrieHolder.
    private volatile int _version = Unset;

    public int? Version
    {
        get
        {
            var current = _version;
            return current == Unset ? null : current;
        }
    }

    public void Set(int version) => _version = version;
}
