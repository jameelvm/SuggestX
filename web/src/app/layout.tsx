import type { Metadata } from "next";

import { SiteNav } from "@/components/layout/site-nav";
import { publicGatewayBaseUrl } from "@/lib/config";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "SuggestX",
    template: "%s · SuggestX",
  },
  description: "A typeahead suggestion system built to internalise system design in practice.",
};

// The doc's "early connection" client-side lever: the browser opens the
// TCP/TLS handshake to the Gateway's origin as soon as the page's <head>
// is parsed, well before the first debounced keystroke would otherwise
// trigger it — a real few-hundred-millisecond win on a cold connection,
// paid once instead of on whichever request happens to be first. Rendered
// server-side in this layout (not injected after hydration) specifically
// so it lands in the initial HTML the browser sees immediately.
const gatewayOrigin = new URL(publicGatewayBaseUrl).origin;

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full antialiased">
      <head>
        <link rel="preconnect" href={gatewayOrigin} />
        <link rel="dns-prefetch" href={gatewayOrigin} />
      </head>
      <body className="flex min-h-full flex-col items-center bg-white pt-24 text-neutral-900">
        <SiteNav />
        {children}
      </body>
    </html>
  );
}
