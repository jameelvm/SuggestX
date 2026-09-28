import { useEffect, useRef, useState } from "react";

/**
 * Re-runs `fetcher` every `intervalMs`, keeping the last successful value
 * on screen through a transient failure rather than clearing it — the
 * insights panel's whole job is showing live pipeline state, and one
 * missed poll shouldn't blank out the last known-good numbers.
 * `lastUpdatedAt` is set only on a successful poll — a caller can use it
 * to show real, visible proof the page is still refreshing, not just
 * that it fetched once on mount.
 * <para>
 * `fetcher` is read through a ref, updated on every render but never
 * itself a dependency of the polling effect — a caller almost always
 * passes a fresh inline function each render, and depending on it
 * directly would restart the interval every render instead of letting it
 * run on its own steady cadence.
 * </para>
 */
export function usePolling<T>(fetcher: () => Promise<T>, intervalMs: number) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null);
  const fetcherRef = useRef(fetcher);

  // Keeps the ref current without touching it during render — refs are
  // only safe to read/write in effects or event handlers, never in the
  // render body itself.
  useEffect(() => {
    fetcherRef.current = fetcher;
  });

  useEffect(() => {
    let cancelled = false;

    async function poll() {
      try {
        const result = await fetcherRef.current();
        if (!cancelled) {
          setData(result);
          setError(null);
          setLastUpdatedAt(new Date());
        }
      } catch {
        if (!cancelled) setError("unavailable");
      }
    }

    void poll();
    const timer = setInterval(poll, intervalMs);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [intervalMs]);

  return { data, error, lastUpdatedAt };
}
