import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, Pencil, Trash2, X, Check } from "lucide-react";
import { api } from "@/lib/apiClient";
import { Button, PageHeader, Empty } from "@/components/ui";
import { cn, isValidUrl } from "@/lib/utils";

interface WebSource {
  id: string;
  name: string;
  url: string;
  trustTier?: string;
  isActive?: boolean;
}

interface FormState {
  name: string;
  url: string;
  trustTier: string;
}

const BLANK: FormState = { name: "", url: "", trustTier: "standard" };
const TIERS = ["standard", "trusted", "restricted"];

function SourceForm({
  initial,
  onSave,
  onCancel,
  saving,
}: {
  initial: FormState;
  onSave: (v: FormState) => void;
  onCancel: () => void;
  saving: boolean;
}) {
  const [v, setV] = useState(initial);
  const urlOk = isValidUrl(v.url);

  return (
    <div className="flex flex-wrap items-end gap-2 border-b border-border bg-secondary/30 px-4 py-3">
      <div className="flex flex-col gap-1">
        <label className="text-xs text-muted">Name</label>
        <input
          className="w-44 rounded-md border border-border bg-background px-2.5 py-1.5 text-sm outline-none focus:border-primary"
          value={v.name}
          onChange={(e) => setV({ ...v, name: e.target.value })}
          placeholder="My source"
        />
      </div>
      <div className="flex flex-col gap-1">
        <label className="text-xs text-muted">URL</label>
        <input
          className={cn(
            "w-64 rounded-md border bg-background px-2.5 py-1.5 text-sm outline-none focus:border-primary",
            v.url && !urlOk ? "border-danger" : "border-border"
          )}
          value={v.url}
          onChange={(e) => setV({ ...v, url: e.target.value })}
          placeholder="https://example.com"
        />
      </div>
      <div className="flex flex-col gap-1">
        <label className="text-xs text-muted">Trust tier</label>
        <select
          className="rounded-md border border-border bg-background px-2.5 py-1.5 text-sm outline-none focus:border-primary"
          value={v.trustTier}
          onChange={(e) => setV({ ...v, trustTier: e.target.value })}
        >
          {TIERS.map((t) => (
            <option key={t} value={t}>{t}</option>
          ))}
        </select>
      </div>
      <Button
        variant="primary"
        disabled={!v.name.trim() || !urlOk || saving}
        onClick={() => onSave(v)}
      >
        <Check className="h-4 w-4" /> Save
      </Button>
      <Button onClick={onCancel}>
        <X className="h-4 w-4" /> Cancel
      </Button>
    </div>
  );
}

export default function WebSourcesScreen() {
  const qc = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  const sources = useQuery({
    queryKey: ["web-sources"],
    queryFn: async () => {
      const res = await api.get<{ items?: WebSource[] } | WebSource[]>(
        "/api/admin/web-sources"
      );
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
  });

  const create = useMutation({
    mutationFn: (body: FormState) => api.post("/api/admin/web-sources", body),
    onSuccess: () => {
      setAdding(false);
      void qc.invalidateQueries({ queryKey: ["web-sources"] });
    },
  });

  const update = useMutation({
    mutationFn: ({ id, body }: { id: string; body: FormState }) =>
      api.put(`/api/admin/web-sources/${id}`, body),
    onSuccess: () => {
      setEditingId(null);
      void qc.invalidateQueries({ queryKey: ["web-sources"] });
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/api/admin/web-sources/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["web-sources"] }),
  });

  const sourceList = sources.data ?? [];

  return (
    <div>
      <PageHeader
        title="Web sources"
        subtitle="Trusted crawl sources and trust tiers"
        actions={
          !adding && (
            <Button variant="primary" onClick={() => setAdding(true)}>
              <Plus className="h-4 w-4" /> Add source
            </Button>
          )
        }
      />

      <div className="overflow-hidden rounded-xl border border-border">
        {adding && (
          <SourceForm
            initial={BLANK}
            onSave={(v) => create.mutate(v)}
            onCancel={() => setAdding(false)}
            saving={create.isPending}
          />
        )}

        {sources.isLoading ? (
          <Empty>Loading…</Empty>
        ) : sources.isError ? (
          <Empty>Could not load web sources.</Empty>
        ) : sourceList.length === 0 && !adding ? (
          <Empty>No web sources configured.</Empty>
        ) : (
          sourceList.map((s) =>
            editingId === s.id ? (
              <SourceForm
                key={s.id}
                initial={{ name: s.name, url: s.url, trustTier: s.trustTier ?? "standard" }}
                onSave={(v) => update.mutate({ id: s.id, body: v })}
                onCancel={() => setEditingId(null)}
                saving={update.isPending}
              />
            ) : (
              <div
                key={s.id}
                className="flex items-center justify-between border-b border-border px-4 py-3 last:border-b-0"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium">{s.name}</span>
                    {s.trustTier && (
                      <span className="rounded-full bg-secondary px-2 py-0.5 text-xs text-muted">
                        {s.trustTier}
                      </span>
                    )}
                    {s.isActive === false && (
                      <span className="rounded-full bg-danger/15 px-2 py-0.5 text-xs text-danger">
                        inactive
                      </span>
                    )}
                  </div>
                  <div className="mt-0.5 truncate text-xs text-muted">{s.url}</div>
                </div>
                <div className="flex gap-1">
                  <button
                    title="Edit"
                    onClick={() => setEditingId(s.id)}
                    className="rounded p-1 text-muted hover:text-foreground"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button
                    title="Delete"
                    disabled={remove.isPending}
                    onClick={() => {
                      if (confirm(`Delete "${s.name}"?`)) remove.mutate(s.id);
                    }}
                    className="rounded p-1 text-muted hover:text-danger disabled:opacity-50"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
            )
          )
        )}
      </div>
    </div>
  );
}
