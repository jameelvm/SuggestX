import { SearchBox } from "@/components/search/search-box";

export default function HomePage() {
  return (
    <main className="flex w-full flex-col items-center gap-8 px-4">
      <h1 className="text-3xl font-semibold text-neutral-800">SuggestX</h1>
      <SearchBox />
    </main>
  );
}
