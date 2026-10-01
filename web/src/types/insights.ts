/** Mirrors `SearchEventsController.DebugStatus` (CollectionService). */
export interface CollectionStatus {
  publishedCount: number;
  lastPublishedAt: string | null;
}

/** Mirrors `AggregatorDebugController.Status`. */
export interface AggregatorStatus {
  batchesProcessed: number;
  entriesProcessed: number;
  lastPollAt: string | null;
  lastProcessedKey: string | null;
}

/** Mirrors `TrieBuilderDebugController.Status`. */
export interface TrieBuilderStatus {
  built: boolean;
  phraseCount: number;
  nodeCount: number;
  currentVersion: number;
  flattenedPrefixCount: number;
  lastBuildAt: string | null;
  recoveredOnStartup: boolean;
  recoveredVersion: number | null;
}

/** Mirrors `SuggestionsController.DebugStatus`. */
export interface SuggestionServiceStatus {
  currentVersion: number | null;
}

/** Mirrors `TrieNodeSnapshot` (TrieBuilder). Segment belongs to the edge into this node, not the node itself — a compressed trie's own shape. */
export interface TrieTreeNode {
  segment: string;
  isTerminal: boolean;
  phrase: string | null;
  frequency: number;
  children: TrieTreeNode[];
}

/** Mirrors `TrieBuilderDebugController.Tree`. */
export interface TrieTreeResponse {
  built: boolean;
  root: TrieTreeNode | null;
}

/** One row of `suggestx-phrase-frequencies`, as `AggregatorDebugController.Frequencies` already ranks it. */
export interface PhraseFrequencyRow {
  phrase: string;
  frequency: number;
}

/** Mirrors `AggregatorDebugController.Frequencies`. */
export interface PhraseFrequenciesResponse {
  count: number;
  entries: PhraseFrequencyRow[];
}
