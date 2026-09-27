using SuggestX.TrieBuilder.Domain;

namespace SuggestX.TrieBuilder.Services;

/// <summary>
/// Holds whichever <see cref="CompressedTrie"/> was most recently built.
/// <c>TrieBuildWorker</c> is the only writer; the debug controller (and,
/// once Module 2 lands, the flattening step that writes to Redis) only
/// ever reads. A reader never sees a half-built trie — <c>Replace</c> only
/// ever receives a trie that <see cref="CompressedTrie.Build"/> already
/// finished constructing.
/// </summary>
public interface ITrieHolder
{
    CompressedTrie? Current { get; }
    void Replace(CompressedTrie trie);
}

public sealed class TrieHolder : ITrieHolder
{
    // volatile so a read on one thread promptly sees a write made on
    // another — reference assignment is already atomic in .NET, but
    // without this a reader could keep observing a stale cached value.
    private volatile CompressedTrie? _current;

    public CompressedTrie? Current => _current;

    public void Replace(CompressedTrie trie) => _current = trie;
}
