import { browserApiFetch } from "@/lib/api/browser-client";
import type { SuggestionResponse } from "@/types/suggestions";

/**
 * The doc's `getSuggestions(prefix)` API, called from the browser after the
 * search box's own debounce (decision: client-side debounce, not a
 * server-side rate limit — one of the doc's own stated latency levers).
 * `encodeURIComponent` matters here specifically because a real prefix can
 * contain a space ("guitar lesso" is the exact over-bound-fallback example
 * verified live in DESIGN.md decision 16).
 */
export function fetchSuggestions(prefix: string): Promise<SuggestionResponse> {
  return browserApiFetch<SuggestionResponse>(
    `/suggestions?prefix=${encodeURIComponent(prefix)}`,
  );
}
