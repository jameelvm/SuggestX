/**
 * Server-side only — deliberately not `NEXT_PUBLIC_*`. Unused by Module 1
 * (the search box is a Client Component, so every call goes through
 * {@link publicGatewayBaseUrl} instead) — kept for symmetry with JameX's own
 * `lib/config.ts` and for whatever Server Component work (the insights
 * panel, most likely) lands in a later module.
 */
export const gatewayBaseUrl =
  process.env.GATEWAY_BASE_URL ?? "http://localhost:9080/api";

/**
 * Ships into the browser bundle — the only reason this needs the
 * `NEXT_PUBLIC_` prefix. The search box calls the Gateway directly from the
 * browser, not through a Next.js server round trip, since there is no
 * session or server-rendered state to attach suggestions to.
 */
export const publicGatewayBaseUrl =
  process.env.NEXT_PUBLIC_GATEWAY_BASE_URL ?? "http://localhost:9080/api";
