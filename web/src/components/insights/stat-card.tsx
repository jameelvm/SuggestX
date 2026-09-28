export function StatCard({ title, rows }: { title: string; rows: [string, string | number][] }) {
  return (
    <div className="rounded-2xl border border-neutral-200 bg-white p-4 shadow-sm">
      <h2 className="mb-2 text-sm font-semibold text-neutral-500">{title}</h2>
      <dl className="space-y-1">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-center justify-between text-sm">
            <dt className="text-neutral-500">{label}</dt>
            <dd className="font-mono text-neutral-900">{value}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
