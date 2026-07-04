import { Fragment, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, RotateCcw, Trash2, X } from "lucide-react";
import axios from "axios";
import { api } from "@/lib/apiClient";
import { Button, PageHeader, Empty } from "@/components/ui";
import IngestionJobFailurePanel from "@/components/IngestionJobFailurePanel";
import {
  filterSupersededIngestionJobs,
  isIngestionFailed,
  markIngestionJobRetrying,
  replaceRetriedIngestionJob,
  type IngestionJobStatus,
  updateIngestionJobInList,
} from "@/lib/ingestionJob";
import { cn, formatLocalDateTime, parseUtcIso } from "@/lib/utils";

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
  const diff = Date.now() - parseUtcIso(iso).getTime();
  const m = Math.floor(diff / 60_000);
  if (m < 1) return "just now";
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

function formatJobLabel(job: IngestionJobStatus) {
  const name = job.inputRef || job.jobId;
  return job.inputType ? `${name} (${job.inputType})` : name;
}

export default function JobsScreen() {
  const qc = useQueryClient();
  const [supersededRetryJobIds, setSupersededRetryJobIds] = useState<Set<string>>(
    () => new Set(),
  );

  const jobs = useQuery({
    queryKey: ["jobs", "cloud"],
    queryFn: async () => {
      const res = await api.get<{ items?: IngestionJobStatus[] } | IngestionJobStatus[]>(
        "/api/ingestion/jobs"
      );
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
    refetchInterval: 15_000,
  });

  const retry = useMutation({
    mutationFn: (id: string) => api.post<IngestionJobStatus>(`/api/ingestion/jobs/${id}/retry`),
    onMutate: async (id) => {
      await Promise.all([
        qc.cancelQueries({ queryKey: ["jobs"] }),
        qc.cancelQueries({ queryKey: ["ingestion", "upload-jobs"] }),
      ]);
      qc.setQueriesData<IngestionJobStatus[]>({ queryKey: ["jobs"] }, (old) =>
        updateIngestionJobInList(old, id, (job) => markIngestionJobRetrying(job)),
      );
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        updateIngestionJobInList(old, id, (job) => markIngestionJobRetrying(job)),
      );
    },
    onSuccess: (res, id) => {
      const retriedJob = res.data;
      if (retriedJob.jobId !== id) {
        setSupersededRetryJobIds((prev) => new Set(prev).add(id));
      }
      qc.setQueriesData<IngestionJobStatus[]>({ queryKey: ["jobs"] }, (old) =>
        replaceRetriedIngestionJob(old, id, retriedJob),
      );
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        replaceRetriedIngestionJob(old, id, retriedJob),
      );
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ["jobs"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
  });

  const [deleteError, setDeleteError] = useState<string | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/api/ingestion/jobs/${id}`),
    onSuccess: () => {
      setDeleteError(null);
      qc.invalidateQueries({ queryKey: ["jobs"] });
    },
    onError: (err: unknown) => {
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        setDeleteError(err.response.data?.detail ?? "Only terminal jobs can be deleted.");
        return;
      }
      setDeleteError(
        err instanceof Error ? err.message : "Failed to delete job."
      );
    },
  });

  const jobList = filterSupersededIngestionJobs(jobs.data, supersededRetryJobIds);
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

      {deleteError && (
        <div className="mb-4 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          <span className="flex-1">{deleteError}</span>
          <button
            className="shrink-0 rounded p-0.5 text-danger hover:bg-danger/20"
            onClick={() => setDeleteError(null)}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}

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
                <Fragment key={j.jobId}>
                  <tr className="border-b border-border hover:bg-secondary/30">
                    <td className="max-w-[480px] px-4 py-3 align-top">
                      <div className="truncate">{formatJobLabel(j)}</div>
                      {isIngestionFailed(j.status) && <IngestionJobFailurePanel job={j} />}
                    </td>
                    <td className="px-4 py-3 align-top">
                      <StatusBadge status={j.status} />
                    </td>
                    <td
                      className="px-4 py-3 align-top text-xs text-muted"
                      title={formatLocalDateTime(j.createdAtUtc)}
                    >
                      {relativeTime(j.createdAtUtc)}
                    </td>
                    <td className="px-4 py-3 align-top">
                      <div className="flex justify-end gap-1">
                        {isIngestionFailed(j.status) && (
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
                </Fragment>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
