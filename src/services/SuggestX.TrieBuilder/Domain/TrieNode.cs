namespace SuggestX.TrieBuilder.Domain;

/// <summary>
/// One node of a compressed trie. <see cref="Segment"/> can hold more than
/// one character — the "Data structure for storing prefixes" chapter's own
/// optimization (merging chains of single-child nodes) — so the node for
/// "UNIVERSAL"/"UNIVERSITY"'s shared prefix is one "VERS" node, not four
/// single-character ones.
/// </summary>
public sealed class TrieNode
{
    public required string Segment { get; set; }

    /// <summary>Keyed by the first character of each child's own <see cref="Segment"/>.</summary>
    public Dictionary<char, TrieNode> Children { get; } = new();

    /// <summary>True if a complete phrase ends exactly at this node.</summary>
    public bool IsTerminal { get; set; }

    /// <summary>Only meaningful when <see cref="IsTerminal"/> — the full phrase this node completes.</summary>
    public string? Phrase { get; set; }

    /// <summary>Only meaningful when <see cref="IsTerminal"/> — how many times this phrase was searched.</summary>
    public long Frequency { get; set; }
}
