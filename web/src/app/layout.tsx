import type { Metadata } from "next";

import { SiteNav } from "@/components/layout/site-nav";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "SuggestX",
    template: "%s · SuggestX",
  },
  description: "A typeahead suggestion system built to internalise system design in practice.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="flex min-h-full flex-col items-center bg-white pt-24 text-neutral-900">
        <SiteNav />
        {children}
      </body>
    </html>
  );
}
