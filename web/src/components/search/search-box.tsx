"use client";

import { useEffect, useRef, useState } from "react";

import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { ApiError } from "@/lib/api/browser-client";
import { submitSearchEvent } from "@/lib/api/search-events";
import { fetchSuggestions } from "@/lib/api/suggestions";
import { recordRecentSearch } from "@/lib/recent-searches";
import type { SuggestionItem } from "@/types/suggestions";

const DEBOUNCE_MS = 300;

// The doc's own "input threshold" latency lever: below this many
// characters, a prefix is too unspecific for a suggestion to be
// meaningfully useful, and firing a request anyway is pure waste — the
// same reasoning as debounce, applied to length instead of time.
const MIN_QUERY_LENGTH = 2;

/**
 * Phase 5's read path, finally exercised from a real browser: type a
 * prefix, wait for typing to pause (the doc's own debounce lever, not a
 * server-side rate limit), then one `GET /api/suggestions` through the
 * Gateway and render whatever comes back. Nothing here computes a
 * suggestion — this component's entire job is turning keystrokes into a
 * debounced prefix and rendering a response, the same "hot path stays
 * trivial" property DESIGN.md decision 2 describes for the backend.
 * <para>
 * Module 2 (this addition): submitting a search — Enter, or picking a
 * rendered suggestion — fires the doc's `addToDatabase(query)` API
 * (`POST /api/search-events`), closing the loop back into the write
 * pipeline every prior phase already built. Never fired per debounced
 * keystroke — only on an actual, deliberate submission, the same
 * distinction CollectionService's own controller comment already draws
 * ("fired once a search is actually submitted... not per keystroke").
 * </para>
 * <para>
 * Module 3 / Phase 7 (this addition): a successful submission is also
 * recorded in this browser's own small recent-search list
 * (`recordRecentSearch`), which every subsequent suggestions fetch sends
 * back to `SuggestionService` for personalization — reordering, never
 * inventing new candidates (DESIGN.md's personalization Q&A). Entries the
 * server reports as personalized get a small "recent" badge so the effect
 * is actually visible, not just present in the response JSON.
 * </para>
 */
export function SearchBox() {
  const [query, setQuery] = useState("");
  const [suggestions, setSuggestions] = useState<SuggestionItem[]>([]);
  const [personalizedPhrases, setPersonalizedPhrases] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [submittedNote, setSubmittedNote] = useState<string | null>(null);
  const debouncedQuery = useDebouncedValue(query, DEBOUNCE_MS);

  // Guards against a slower earlier request resolving after a faster later
  // one and clobbering its (more current) results — a real race, not a
  // hypothetical one, once debounce still lets two requests overlap in
  // flight (e.g. a fast network reordering two responses).
  const latestRequestId = useRef(0);

  const trimmedQuery = debouncedQuery.trim();

  useEffect(() => {
    // Nothing to fetch below the input threshold — deliberately not
    // clearing `suggestions`/`error` state here too: that would be a
    // setState call synchronous within the effect body for no benefit,
    // since the render below already derives what's actually shown from
    // `trimmedQuery` directly rather than trusting stale fetched state.
    if (trimmedQuery.length < MIN_QUERY_LENGTH) return;

    const requestId = ++latestRequestId.current;

    fetchSuggestions(trimmedQuery)
      .then((response) => {
        if (requestId !== latestRequestId.current) return;
        setSuggestions(response.suggestions);
        setPersonalizedPhrases(new Set(response.personalizedPhrases));
        setError(null);
      })
      .catch((err: unknown) => {
        if (requestId !== latestRequestId.current) return;
        setSuggestions([]);
        setPersonalizedPhrases(new Set());
        setError(
          err instanceof ApiError
            ? `Suggestions unavailable (${err.status})`
            : "Suggestions unavailable",
        );
      });
  }, [trimmedQuery]);

  const visibleSuggestions = trimmedQuery.length < MIN_QUERY_LENGTH ? [] : suggestions;
  const visibleError = trimmedQuery.length < MIN_QUERY_LENGTH ? null : error;

  function submit(term: string) {
    const trimmed = term.trim();
    if (trimmed.length === 0) return;

    submitSearchEvent(trimmed)
      .then(() => {
        setSubmittedNote(`Logged "${trimmed}" — watch it flow through the pipeline`);
        recordRecentSearch(trimmed);
      })
      .catch(() => setSubmittedNote(null));
  }

  function handleChange(value: string) {
    setQuery(value);
    setSubmittedNote(null);
  }

  function handleKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter") submit(query);
  }

  function handleSuggestionClick(phrase: string) {
    setQuery(phrase);
    submit(phrase);
  }

  return (
    <div className="mx-auto w-full max-w-xl">
      <input
        type="text"
        value={query}
        onChange={(event) => handleChange(event.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="Search…"
        className="w-full rounded-full border border-neutral-300 px-5 py-3 text-base outline-none focus:border-neutral-500"
        aria-label="Search"
        autoComplete="off"
      />

      {visibleError && <p className="mt-2 text-sm text-red-600">{visibleError}</p>}
      {submittedNote && <p className="mt-2 text-sm text-emerald-600">{submittedNote}</p>}

      {visibleSuggestions.length > 0 && (
        <ul className="mt-2 divide-y divide-neutral-100 rounded-2xl border border-neutral-200 bg-white shadow-sm">
          {visibleSuggestions.map((item) => (
            <li key={item.phrase}>
              <button
                type="button"
                onClick={() => handleSuggestionClick(item.phrase)}
                className="flex w-full items-center justify-between px-5 py-2.5 text-left text-sm text-neutral-800 hover:bg-neutral-50"
              >
                <span className="flex items-center gap-2">
                  {item.phrase}
                  {personalizedPhrases.has(item.phrase) && (
                    <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                      recent
                    </span>
                  )}
                </span>
                <span className="text-neutral-400">{item.frequency}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
