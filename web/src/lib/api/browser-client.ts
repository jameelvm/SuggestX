import { publicGatewayBaseUrl } from "@/lib/config";
import { requestJson } from "@/lib/api/errors";

export { ApiError } from "@/lib/api/errors";

/**
 * Every browser-side Gateway call goes through here — just
 * {@link requestJson} pinned to the browser-reachable base URL. No auth
 * header to attach (unlike JameX's `browser-client.ts`): this system has no
 * identity concept at all, matching CLAUDE.md's own note that the design
 * doc never introduces one.
 */
export function browserApiFetch<T>(
  path: string,
  init?: RequestInit,
): Promise<T> {
  return requestJson<T>(publicGatewayBaseUrl, path, init);
}
