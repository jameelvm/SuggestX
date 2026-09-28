import type { NextConfig } from "next";

// Nothing to configure yet — no images, no rewrites. Every backend call
// goes through the Gateway's own base URL (see src/lib/config.ts), not a
// Next.js rewrite, so the same code path works whether the Gateway is a
// container or a locally-debugged process (see DEBUGGING.md).
const nextConfig: NextConfig = {};

export default nextConfig;
