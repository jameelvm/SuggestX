import Link from "next/link";

export function SiteNav() {
  return (
    <nav className="fixed top-0 z-10 flex w-full justify-center gap-6 border-b border-neutral-100 bg-white/90 py-3 backdrop-blur">
      <Link href="/" className="text-sm font-medium text-neutral-600 hover:text-neutral-900">
        Search
      </Link>
      <Link href="/insights" className="text-sm font-medium text-neutral-600 hover:text-neutral-900">
        Insights
      </Link>
    </nav>
  );
}
