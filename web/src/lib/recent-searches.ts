const STORAGE_KEY = "suggestx.recentSearches";
const MAX_RECENT = 10;

/**
 * The client-side half of Phase 7 personalization (DESIGN.md's "shared
 * trie, blended with a client-cached recent-search list at merge time in
 * SuggestionService" answer): this system has no identity concept, so
 * there is no server-side user profile to key by — the browser is the
 * only place "this person's own history" can live, and it resends that
 * small list on every suggestions request rather than the server storing
 * anything per-user.
 * <para>
 * `localStorage` here is a genuine per-viewer convenience, not state
 * anything else depends on for correctness — every read/write is wrapped
 * defensively, since it can throw or silently no-op (private browsing,
 * quota, disabled storage) and the search box must keep working either
 * way, just without personalization that session.
 * </para>
 */
export function getRecentSearches(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((entry): entry is string => typeof entry === "string") : [];
  } catch {
    return [];
  }
}

/** Most-recent-first, deduplicated case-insensitively, capped at `MAX_RECENT`. */
export function recordRecentSearch(phrase: string): void {
  const trimmed = phrase.trim();
  if (trimmed.length === 0) return;

  try {
    const withoutDuplicate = getRecentSearches().filter((entry) => entry.toLowerCase() !== trimmed.toLowerCase());
    const updated = [trimmed, ...withoutDuplicate].slice(0, MAX_RECENT);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
  } catch {
    // Storage failures are silently ignored — see the module doc comment.
  }
}
