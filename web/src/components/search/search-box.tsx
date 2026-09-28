"use client";

import { useEffect, useRef, useState } from "react";

import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { ApiError } from "@/lib/api/browser-client";
import { fetchSuggestions } from "@/lib/api/suggestions";
import type { SuggestionItem } from "@/types/suggestions";

const DEBOUNCE_MS = 300;

/**
 * Phase 5's whole point, finally exercised from a real browser: type a
 * prefix, wait for typing to pause (the doc's own debounce lever, not a
 * server-side rate limit), then one `GET /api/suggestions` through the
 * Gateway and render whatever comes back. Nothing here computes a
 * suggestion — this component's entire job is turning keystrokes into a
 * debounced prefix and rendering a response, the same "hot path stays
 * trivial" property DESIGN.md decision 2 describes for the backend.
 */
export function SearchBox() {
  const [query, setQuery] = useState("");
  const [suggestions, setSuggestions] = useState<SuggestionItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const debouncedQuery = useDebouncedValue(query, DEBOUNCE_MS);

  // Guards against a slower earlier request resolving after a faster later
  // one and clobbering its (more current) results — a real race, not a
  // hypothetical one, once debounce still lets two requests overlap in
  // flight (e.g. a fast network reordering two responses).
  const latestRequestId = useRef(0);

  const trimmedQuery = debouncedQuery.trim();

  useEffect(() => {
    // Nothing to fetch for an empty query — deliberately not clearing
    // `suggestions`/`error` state here too: that would be a setState call
    // synchronous within the effect body for no benefit, since the render
    // below already derives what's actually shown from `trimmedQuery`
    // directly rather than trusting stale fetched state in this case.
    if (trimmedQuery.length === 0) return;

    const requestId = ++latestRequestId.current;

    fetchSuggestions(trimmedQuery)
      .then((response) => {
        if (requestId !== latestRequestId.current) return;
        setSuggestions(response.suggestions);
        setError(null);
      })
      .catch((err: unknown) => {
        if (requestId !== latestRequestId.current) return;
        setSuggestions([]);
        setError(
          err instanceof ApiError
            ? `Suggestions unavailable (${err.status})`
            : "Suggestions unavailable",
        );
      });
  }, [trimmedQuery]);

  const visibleSuggestions = trimmedQuery.length === 0 ? [] : suggestions;
  const visibleError = trimmedQuery.length === 0 ? null : error;

  return (
    <div className="mx-auto w-full max-w-xl">
      <input
        type="text"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        placeholder="Search…"
        className="w-full rounded-full border border-neutral-300 px-5 py-3 text-base outline-none focus:border-neutral-500"
        aria-label="Search"
        autoComplete="off"
      />

      {visibleError && <p className="mt-2 text-sm text-red-600">{visibleError}</p>}

      {visibleSuggestions.length > 0 && (
        <ul className="mt-2 divide-y divide-neutral-100 rounded-2xl border border-neutral-200 bg-white shadow-sm">
          {visibleSuggestions.map((item) => (
            <li
              key={item.phrase}
              className="flex items-center justify-between px-5 py-2.5 text-sm text-neutral-800"
            >
              <span>{item.phrase}</span>
              <span className="text-neutral-400">{item.frequency}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
