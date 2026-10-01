"use client";

import { FrequencyTable } from "@/components/insights/frequency-table";
import { useNow } from "@/hooks/use-now";
import { usePolling } from "@/hooks/use-polling";
import { fetchPhraseFrequencies } from "@/lib/api/insights";

const POLL_MS = 2000;

function formatAgo(from: Date | null, now: number): string {
  if (!from) return "waiting for first refresh…";
  const seconds = Math.max(0, Math.round((now - from.getTime()) / 1000));
  return seconds === 0 ? "just now" : `${seconds}s ago`;
}

/**
 * A real, live view of `suggestx-phrase-frequencies` — Aggregator's own
 * DynamoDB table, the thing TrieBuilder reads every cycle to build the
 * trie the search page ultimately serves from. Polls
 * `GET /_debug/frequencies` (a real full `Scan`, the same shape as
 * TrieBuilder's own reader) every 2s, so submitting a search on the
 * search page and watching its row appear or its count climb here is a
 * genuine, live demonstration of the aggregation step — not a snapshot
 * taken once on page load.
 */
export default function FrequenciesPage() {
  const frequencies = usePolling(fetchPhraseFrequencies, POLL_MS);
  const now = useNow();

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 px-4 pb-16">
      <div>
        <h1 className="text-3xl font-semibold text-neutral-800">Phrase Frequencies</h1>
        <p className="mt-1 text-sm text-neutral-500">
          Aggregator&apos;s own table — every phrase it has counted, polled every {POLL_MS / 1000}s.
          Submit a search on the search page and watch its count climb here.
        </p>
        <p className="mt-1 flex items-center gap-1.5 text-xs text-neutral-400">
          <span className="inline-block h-1.5 w-1.5 rounded-full bg-emerald-500" />
          Refreshed {formatAgo(frequencies.lastUpdatedAt, now)}
        </p>
      </div>

      {frequencies.data ? (
        <FrequencyTable rows={frequencies.data.entries} />
      ) : (
        <p className="text-sm text-neutral-400">Loading…</p>
      )}
    </main>
  );
}
