import { useEffect, useState } from "react";

/**
 * The current time, re-read every `intervalMs` — exists purely so a
 * component can derive a live "N seconds ago" from a fixed timestamp
 * (see `usePolling`'s `lastUpdatedAt`) without that timestamp itself
 * needing to change. 1s by default: fine-grained enough to visibly tick,
 * far cheaper than re-running an actual data poll that often.
 */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);

  return now;
}
