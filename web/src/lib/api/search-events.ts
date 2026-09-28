import { browserApiMutate } from "@/lib/api/browser-client";

/**
 * The doc's `addToDatabase(query)` API — fired once a search is actually
 * submitted (Enter, or picking a rendered suggestion), never per debounced
 * keystroke. This is what closes the loop: the event this call logs is
 * what Collection → Aggregator → TrieBuilder eventually turn back into a
 * suggestion, the same pipeline every prior phase already built and
 * verified — this is the first time a browser action triggers it.
 */
export function submitSearchEvent(query: string): Promise<void> {
  return browserApiMutate("/search-events", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query }),
  });
}
