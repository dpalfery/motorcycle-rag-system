import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, RotateCcw, Trash2 } from "lucide-react";
import { api } from "@/lib/apiClient";
import { Button, PageHeader, Empty } from "@/components/ui";
import { cn } from "@/lib/utils";

interface IngestionJob {
  jobId: string;
  fileName?: string;
  documentType?: string;
  status: string;
  createdAt: string;
  updatedAt?: string;
  error?: string;
}

const STATUS_COLOR: Record<string, string> = {
  completed:   "bg-success/15 text-success",
  complete:    "bg-success/15 text-success",
  done:        "bg-success/15 text-success",
  succeeded:   "bg-success/15 text-success",
  failed:      "bg-danger/15 text-danger",
  error:       "bg-danger/15 text-danger",
  cancelled:   "bg-danger/15 text-danger",
  processing:  "bg-primary/15 text-primary",
  queued:      "bg-warning/15 text-warning",
  pending:     "bg-warning/15 text-warning",
};

function StatusBadge({ status }: { status: string }) {
  const cls = STATUS_COLOR[status.toLowerCase()] ?? "bg-secondary text-muted";
  return (
    <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-medium", cls)}>
      {status}
    </span>
  );
}

function relativeTime(iso: string) {
  const diff = Date.now() - new Date(iso).getTime();
  const m = Math.floor(diff / 60_000);
  if (m < 1) return "just now";
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

export default function JobsScreen() {
  const qc = useQueryClient();

  const jobs = useQuery({
    queryKey: ["jobs", "cloud"],
    queryFn: async () => {
      const res = await api.get<{ items?: IngestionJob[] } | IngestionJob[]>(
        "/api/ingestion/jobs"
      );
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
    refetchInterval: 15_000,
  });

  const retry = useMutation({
    mutationFn: (id: string) => api.put(`/api/ingestion/jobs/${id}/retry`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["jobs"] }),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/api/ingestion/jobs/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["jobs"] }),
  });

  const jobList = jobs.data ?? [];
  const active = jobList.filter((j) =>
    ["processing", "queued", "pending"].includes(j.status.toLowerCase())
  ).length;

  return (
    <div>
      <PageHeader
        title="Jobs"
        subtitle="Ingestion and processing jobs"
        actions={
          <Button onClick={() => qc.invalidateQueries({ queryKey: ["jobs"] })}>
            <RefreshCw className="h-4 w-4" /> Refresh
          </Button>
        }
      />

      <div className="mb-4 flex gap-3">
        <div className="rounded-lg bg-secondary/60 px-4 py-2.5">
          <div className="text-xs text-muted">Total</div>
          <div className="text-lg font-medium">{jobList.length}</div>
        </div>
        <div className="rounded-lg bg-secondary/60 px-4 py-2.5">
          <div className="text-xs text-muted">Active</div>
          <div className={cn("text-lg font-medium", active > 0 ? "text-primary" : "")}>
            {active}
          </div>
        </div>
      </div>

      <div className="overflow-hidden rounded-xl border border-border">
        {jobs.isLoading ? (
          <Empty>Loading jobs…</Empty>
        ) : jobs.isError ? (
          <Empty>Could not load jobs.</Empty>
        ) : jobList.length === 0 ? (
          <Empty>No jobs found.</Empty>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border text-left text-xs text-muted">
                <th className="px-4 py-2.5 font-medium">File</th>
                <th className="px-4 py-2.5 font-medium">Status</th>
                <th className="px-4 py-2.5 font-medium">Created</th>
                <th className="px-4 py-2.5 font-medium" />
              </tr>
            </thead>
            <tbody>
              {jobList.map((j) => (
                <tr
                  key={j.jobId}
                  className="border-b border-border last:border-b-0 hover:bg-secondary/30"
                >
                  <td className="max-w-[280px] px-4 py-3">
                    <div className="truncate">{j.fileName ?? j.jobId}</div>
                    {j.error && (
                      <div className="truncate text-xs text-danger">{j.error}</div>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <StatusBadge status={j.status} />
                  </td>
                  <td className="px-4 py-3 text-xs text-muted">
                    {relativeTime(j.createdAt)}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex justify-end gap-1">
                      {["failed", "error"].includes(j.status.toLowerCase()) && (
                        <button
                          title="Retry"
                          disabled={retry.isPending}
                          onClick={() => retry.mutate(j.jobId)}
                          className="rounded p-1 text-muted hover:text-primary disabled:opacity-50"
                        >
                          <RotateCcw className="h-4 w-4" />
                        </button>
                      )}
                      <button
                        title="Delete"
                        disabled={remove.isPending}
                        onClick={() => remove.mutate(j.jobId)}
                        className="rounded p-1 text-muted hover:text-danger disabled:opacity-50"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
