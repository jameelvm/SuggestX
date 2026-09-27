namespace SuggestX.TrieBuilder.Domain;

/// <summary>
/// An in-memory compressed trie over every known phrase and its frequency.
/// Built fresh each cycle by <c>TrieBuildWorker</c> from
/// suggestx-phrase-frequencies, then walked once to precompute every
/// prefix's top-N (Phase 4 Module 2) — this class itself only needs to
/// answer "what are the best matches for this prefix," not know anything
/// about Redis, S3, or ZooKeeper. See CLAUDE.md: this is the only place in
/// the whole system an actual trie data structure exists.
/// </summary>
public sealed class CompressedTrie
{
    private readonly TrieNode _root = new() { Segment = "" };

    public int NodeCount { get; private set; }
    public int PhraseCount { get; private set; }

    /// <summary>
    /// Builds a complete, ready-to-query trie in one call — there is no
    /// partially-built state a reader could ever observe, since the trie
    /// held by <c>ITrieHolder</c> is only ever swapped in as a finished
    /// object (the same blue/green principle as DESIGN.md decision 8,
    /// applied in-process instead of across Redis versions).
    /// </summary>
    public static CompressedTrie Build(IEnumerable<(string Phrase, long Frequency)> frequencies)
    {
        var trie = new CompressedTrie();
        foreach (var (phrase, frequency) in frequencies)
        {
            if (phrase.Length == 0) continue;
            trie.Insert(phrase, frequency);
        }

        Compress(trie._root);
        trie.ComputeStats();
        return trie;
    }

    /// <summary>
    /// Walks to the node representing <paramref name="prefix"/> and returns
    /// its descendant phrases ranked by frequency. Empty when the prefix
    /// isn't a path in the trie at all — a real "no suggestions" case, not
    /// an error.
    /// </summary>
    public IReadOnlyList<(string Phrase, long Frequency)> GetTopMatches(string prefix, int limit)
    {
        var normalized = prefix.Trim().ToLowerInvariant();
        if (normalized.Length == 0 || limit <= 0) return [];

        var node = FindNode(_root, normalized);
        if (node is null) return [];

        var matches = new List<(string Phrase, long Frequency)>();
        CollectTerminals(node, matches);

        return matches
            .OrderByDescending(m => m.Frequency)
            // Deterministic tie-break so two builds over identical data
            // produce identical output, not an arbitrary dictionary order.
            .ThenBy(m => m.Phrase, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    private void Insert(string phrase, long frequency)
    {
        // Uncompressed insert — one character per node. Compression runs as
        // a separate pass over the whole trie once every phrase is in,
        // rather than trying to keep segments merged during insertion
        // itself. The doc presents compression as its own transformation
        // step on an already-built trie, not as part of insertion, and
        // splitting an existing multi-character segment mid-insert is real
        // extra complexity this two-phase approach avoids entirely.
        var node = _root;
        foreach (var ch in phrase)
        {
            if (!node.Children.TryGetValue(ch, out var child))
            {
                child = new TrieNode { Segment = ch.ToString() };
                node.Children[ch] = child;
            }
            node = child;
        }

        node.IsTerminal = true;
        node.Phrase = phrase;
        node.Frequency = frequency;
    }

    /// <summary>
    /// Merges chains of single-child, non-terminal nodes into one node with
    /// a multi-character segment — "UNI" -> "V" -> "E" -> "R" -> "S" becomes
    /// one "VERS" node. A terminal node is never merged past, even if it
    /// has exactly one child: it has to stay individually addressable,
    /// since it's a valid stopping point with its own frequency (e.g. "CAT"
    /// is terminal but has a child continuing to "CATS").
    /// </summary>
    private static void Compress(TrieNode node)
    {
        foreach (var child in node.Children.Values)
        {
            Compress(child);
        }

        foreach (var key in node.Children.Keys.ToList())
        {
            var child = node.Children[key];
            while (!child.IsTerminal && child.Children.Count == 1)
            {
                var grandchild = child.Children.Values.Single();
                child.Segment += grandchild.Segment;
                child.Children.Clear();
                foreach (var (gcKey, gcNode) in grandchild.Children)
                    child.Children[gcKey] = gcNode;
                child.IsTerminal = grandchild.IsTerminal;
                child.Phrase = grandchild.Phrase;
                child.Frequency = grandchild.Frequency;
            }
        }
    }

    private static TrieNode? FindNode(TrieNode node, string remaining)
    {
        if (remaining.Length == 0) return node;

        if (!node.Children.TryGetValue(remaining[0], out var child)) return null;

        var segment = child.Segment;
        if (remaining.Length <= segment.Length)
        {
            // The whole remaining prefix must land inside this one segment —
            // e.g. prefix "uni" against segment "univ" is a match partway
            // through the node; the node's descendants are still the answer.
            return segment.StartsWith(remaining, StringComparison.Ordinal) ? child : null;
        }

        // The prefix reaches past this segment — it must match it exactly
        // before continuing further down.
        if (!remaining.StartsWith(segment, StringComparison.Ordinal)) return null;

        return FindNode(child, remaining[segment.Length..]);
    }

    private static void CollectTerminals(TrieNode node, List<(string, long)> results)
    {
        if (node.IsTerminal) results.Add((node.Phrase!, node.Frequency));

        foreach (var child in node.Children.Values)
            CollectTerminals(child, results);
    }

    private void ComputeStats()
    {
        NodeCount = 0;
        PhraseCount = 0;
        Count(_root);

        void Count(TrieNode node)
        {
            NodeCount++;
            if (node.IsTerminal) PhraseCount++;
            foreach (var child in node.Children.Values) Count(child);
        }
    }
}
