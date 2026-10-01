"use client";

import { useMemo, useState } from "react";

import type { PhraseFrequencyRow } from "@/types/insights";

type SortColumn = "phrase" | "frequency";
type SortDirection = "asc" | "desc";

function SortButton({
  label,
  column,
  activeColumn,
  direction,
  onClick,
}: {
  label: string;
  column: SortColumn;
  activeColumn: SortColumn;
  direction: SortDirection;
  onClick: (column: SortColumn) => void;
}) {
  const isActive = column === activeColumn;
  return (
    <button
      type="button"
      onClick={() => onClick(column)}
      className="flex items-center gap-1 text-left font-semibold text-neutral-500 hover:text-neutral-800"
    >
      {label}
      <span className={isActive ? "text-neutral-800" : "text-neutral-300"}>
        {isActive && direction === "asc" ? "↑" : "↓"}
      </span>
    </button>
  );
}

/**
 * A real, live table of `suggestx-phrase-frequencies` — every phrase
 * Aggregator has ever counted and its running total, fetched fresh from
 * `GET /_debug/frequencies` on every poll (see the insights page). Sorting
 * and the search filter are both purely client-side over the already-
 * fetched rows: this project's demo-scale data (a few dozen phrases at
 * most) makes a server-side search/sort endpoint real, unnecessary
 * complexity — the same "toy-scale simplification" this codebase already
 * makes elsewhere (TrieBuilder's own full-table debug dumps).
 */
export function FrequencyTable({ rows }: { rows: PhraseFrequencyRow[] }) {
  const [filter, setFilter] = useState("");
  const [sortColumn, setSortColumn] = useState<SortColumn>("frequency");
  const [sortDirection, setSortDirection] = useState<SortDirection>("desc");

  function handleSort(column: SortColumn) {
    if (column === sortColumn) {
      setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortColumn(column);
      setSortDirection(column === "frequency" ? "desc" : "asc");
    }
  }

  const visibleRows = useMemo(() => {
    const normalizedFilter = filter.trim().toLowerCase();
    const filtered = normalizedFilter
      ? rows.filter((row) => row.phrase.toLowerCase().includes(normalizedFilter))
      : rows;

    const sorted = [...filtered].sort((a, b) => {
      const comparison =
        sortColumn === "phrase"
          ? a.phrase.localeCompare(b.phrase)
          : a.frequency - b.frequency;
      return sortDirection === "asc" ? comparison : -comparison;
    });

    return sorted;
  }, [rows, filter, sortColumn, sortDirection]);

  return (
    <div>
      <div className="mb-3 flex items-center justify-between gap-4">
        <input
          type="text"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Filter phrases…"
          className="w-64 rounded-full border border-neutral-300 px-4 py-1.5 text-sm outline-none focus:border-neutral-500"
        />
        <span className="text-xs text-neutral-400">
          {visibleRows.length} of {rows.length} phrases
        </span>
      </div>

      <div className="overflow-x-auto rounded-xl border border-neutral-200">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-neutral-200 bg-neutral-50">
            <tr>
              <th className="px-4 py-2">
                <SortButton
                  label="Phrase"
                  column="phrase"
                  activeColumn={sortColumn}
                  direction={sortDirection}
                  onClick={handleSort}
                />
              </th>
              <th className="px-4 py-2">
                <SortButton
                  label="Frequency"
                  column="frequency"
                  activeColumn={sortColumn}
                  direction={sortDirection}
                  onClick={handleSort}
                />
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-neutral-100">
            {visibleRows.map((row) => (
              <tr key={row.phrase} className="hover:bg-neutral-50">
                <td className="px-4 py-2 text-neutral-800">{row.phrase}</td>
                <td className="px-4 py-2 font-mono text-neutral-600">{row.frequency}</td>
              </tr>
            ))}
            {visibleRows.length === 0 && (
              <tr>
                <td colSpan={2} className="px-4 py-6 text-center text-neutral-400">
                  No phrases match &quot;{filter}&quot;.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
