import { browserApiFetch } from "@/lib/api/browser-client";
import { getRecentSearches } from "@/lib/recent-searches";
import type { SuggestionResponse } from "@/types/suggestions";

// Matches SuggestionServiceOptions.CacheMaxAgeSeconds (the `Cache-Control:
// public, max-age=5` the server itself already sends) — this in-memory
// cache is the same "edge cache" lever applied one hop earlier, at the
// browser tab itself, and it would be dishonest for it to hold a response
// longer than the server's own claimed freshness window.
const CACHE_TTL_MS = 5000;
// A crude bound, not a real LRU: at this project's demo scale, a session
// realistically produces a few dozen distinct prefixes at most, so simply
// dropping the whole cache past a generous ceiling is a fine, simple
// trade — see the doc's own "toy-scale simplification" pattern elsewhere
// (the full DynamoDB `Scan`, the full trie snapshot dump).
const MAX_CACHE_ENTRIES = 100;

const cache = new Map<string, { response: SuggestionResponse; cachedAt: number }>();

/**
 * The doc's own "local cache" client-side lever: re-typing a prefix
 * already fetched this session (backspacing and retyping is common) skips
 * the network round trip entirely. Keyed by the exact query string,
 * including `recent` — a change in the browser's own recent-search list
 * (a new submission) naturally produces a new key, so a stale
 * pre-personalization entry is never served after that; it just becomes
 * an orphaned, harmless entry that ages out on its own via `CACHE_TTL_MS`.
 */
function readCache(key: string): SuggestionResponse | null {
  const entry = cache.get(key);
  if (!entry) return null;

  if (Date.now() - entry.cachedAt > CACHE_TTL_MS) {
    cache.delete(key);
    return null;
  }

  return entry.response;
}

function writeCache(key: string, response: SuggestionResponse): void {
  if (cache.size >= MAX_CACHE_ENTRIES) cache.clear();
  cache.set(key, { response, cachedAt: Date.now() });
}

/**
 * The doc's `getSuggestions(prefix)` API, called from the browser after the
 * search box's own debounce (decision: client-side debounce, not a
 * server-side rate limit — one of the doc's own stated latency levers).
 * Also sends the browser's own recent-search list on every call (Phase 7
 * personalization) — there's no session to attach it to server-side, so
 * it's resent fresh each request rather than remembered anywhere but this
 * browser. `URLSearchParams` handles encoding both values, including a
 * prefix with a space ("guitar lesso" is the exact over-bound-fallback
 * example verified live in DESIGN.md decision 16).
 */
export function fetchSuggestions(prefix: string): Promise<SuggestionResponse> {
  const params = new URLSearchParams({ prefix });

  const recent = getRecentSearches();
  if (recent.length > 0) params.set("recent", recent.join(","));

  const key = params.toString();

  const cached = readCache(key);
  if (cached) return Promise.resolve(cached);

  return browserApiFetch<SuggestionResponse>(`/suggestions?${key}`).then((response) => {
    writeCache(key, response);
    return response;
  });
}
