import { useEffect, useState } from "react";

/**
 * The doc's own stated client-side latency lever: don't fire a request per
 * keystroke, wait for typing to pause first. Generic over `T` rather than
 * string-specific, in case a later module debounces something other than
 * the raw query text (e.g. a parsed filter).
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
