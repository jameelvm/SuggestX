"use client";

import { StatCard } from "@/components/insights/stat-card";
import { TrieGraph } from "@/components/insights/trie-graph";
import { useNow } from "@/hooks/use-now";
import { usePolling } from "@/hooks/use-polling";
import {
  fetchAggregatorStatus,
  fetchCollectionStatus,
  fetchSuggestionServiceStatus,
  fetchTrieBuilderStatus,
  fetchTrieTree,
} from "@/lib/api/insights";

const POLL_MS = 2000;

function formatTime(iso: string | null | undefined): string {
  return iso ? new Date(iso).toLocaleTimeString() : "…";
}

/** The most recent of several poll timestamps, or null if none have landed yet. */
function mostRecent(times: (Date | null)[]): Date | null {
  const known = times.filter((t): t is Date => t !== null);
  if (known.length === 0) return null;
  return new Date(Math.max(...known.map((t) => t.getTime())));
}

/** "just now" / "3s ago" / "12s ago" — real, visible proof the panel is still polling, not a static page. */
function formatAgo(from: Date | null, now: number): string {
  if (!from) return "waiting for first refresh…";
  const seconds = Math.max(0, Math.round((now - from.getTime()) / 1000));
  return seconds === 0 ? "just now" : `${seconds}s ago`;
}

/**
 * Phase 6 Module 3: a real-time view of the write pipeline every prior
 * phase already built, and the compressed trie it feeds — nothing here
 * computes anything, every number and every node comes from a live poll
 * of the same `/_debug/*` endpoints each service already exposed for its
 * own module-by-module verification. Watching a submitted search (the
 * home page's own Module 2) move through Collection → Aggregator →
 * TrieBuilder here, live, is this module's whole point.
 */
export default function InsightsPage() {
  const collection = usePolling(fetchCollectionStatus, POLL_MS);
  const aggregator = usePolling(fetchAggregatorStatus, POLL_MS);
  const trieBuilder = usePolling(fetchTrieBuilderStatus, POLL_MS);
  const suggestionService = usePolling(fetchSuggestionServiceStatus, POLL_MS);
  const tree = usePolling(fetchTrieTree, POLL_MS);

  const now = useNow();
  const lastRefreshed = mostRecent([
    collection.lastUpdatedAt,
    aggregator.lastUpdatedAt,
    trieBuilder.lastUpdatedAt,
    suggestionService.lastUpdatedAt,
    tree.lastUpdatedAt,
  ]);

  return (
    <main className="flex w-full flex-col gap-6 px-6 pb-16 md:px-10 lg:px-16">
      <div>
        <h1 className="text-3xl font-semibold text-neutral-800">Insights</h1>
        <p className="mt-1 text-sm text-neutral-500">
          Live pipeline state, polled every {POLL_MS / 1000}s. Submit a search on the
          search page, then watch it move through Collection → Aggregator → TrieBuilder here.
        </p>
        <p className="mt-1 flex items-center gap-1.5 text-xs text-neutral-400">
          <span className="inline-block h-1.5 w-1.5 rounded-full bg-emerald-500" />
          Refreshed {formatAgo(lastRefreshed, now)}
        </p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          title="Collection"
          rows={[
            ["Published", collection.data?.publishedCount ?? "…"],
            ["Last published", formatTime(collection.data?.lastPublishedAt)],
          ]}
        />
        <StatCard
          title="Aggregator"
          rows={[
            ["Batches processed", aggregator.data?.batchesProcessed ?? "…"],
            ["Entries processed", aggregator.data?.entriesProcessed ?? "…"],
            ["Last poll", formatTime(aggregator.data?.lastPollAt)],
          ]}
        />
        <StatCard
          title="Trie Builder"
          rows={[
            ["Version", trieBuilder.data?.currentVersion ?? "…"],
            ["Phrases", trieBuilder.data?.phraseCount ?? "…"],
            ["Nodes", trieBuilder.data?.nodeCount ?? "…"],
            ["Flattened prefixes", trieBuilder.data?.flattenedPrefixCount ?? "…"],
            ["Last build", formatTime(trieBuilder.data?.lastBuildAt)],
          ]}
        />
        <StatCard
          title="Suggestion Service"
          rows={[["Serving version", suggestionService.data?.currentVersion ?? "…"]]}
        />
      </div>

      <div className="rounded-2xl border border-neutral-200 bg-white p-4 shadow-sm">
        <h2 className="mb-2 text-sm font-semibold text-neutral-500">
          Trie structure — version {trieBuilder.data?.currentVersion ?? "…"}
        </h2>
        {tree.data?.root ? (
          <TrieGraph root={tree.data.root} />
        ) : (
          <p className="text-sm text-neutral-400">No trie built yet.</p>
        )}
      </div>
    </main>
  );
}
