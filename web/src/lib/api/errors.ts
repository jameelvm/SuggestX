/** Thrown for any non-2xx Gateway response. Carries the status so callers can branch on it. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly path: string,
  ) {
    super(`Gateway request to "${path}" failed with status ${status}`);
    this.name = "ApiError";
  }
}

/**
 * The core every Gateway client shares. Takes the base URL as a parameter
 * rather than importing one directly, mirroring JameX's own
 * `lib/api/errors.ts` — server-side and browser-side callers deliberately
 * read it from different environment variables.
 */
export async function requestJson<T>(
  baseUrl: string,
  path: string,
  init?: RequestInit,
): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    throw new ApiError(response.status, path);
  }

  return (await response.json()) as T;
}

/**
 * For mutations whose success response carries no body — `POST
 * /search-events` returns a bare `202`, mirroring JameX's own
 * `requestVoid`. Deliberately separate from {@link requestJson} rather
 * than guessing based on status code: a caller expecting real JSON back
 * should still get a clear parse failure if a body it needed turns out to
 * be empty, not have that silently coerced away.
 */
export async function requestVoid(
  baseUrl: string,
  path: string,
  init?: RequestInit,
): Promise<void> {
  const response = await fetch(`${baseUrl}${path}`, init);

  if (!response.ok) {
    throw new ApiError(response.status, path);
  }
}
