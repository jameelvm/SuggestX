import { browserApiFetch } from "@/lib/api/browser-client";
import { getRecentSearches } from "@/lib/recent-searches";
import type { SuggestionResponse } from "@/types/suggestions";

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

  return browserApiFetch<SuggestionResponse>(`/suggestions?${params.toString()}`);
}
