import { Fragment, useCallback, useMemo, useState, type ReactNode } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, RotateCcw, Trash2, X, CheckCircle2, Pencil } from "lucide-react";
import axios from "axios";
import { api } from "@/lib/apiClient";
import { formatAdminError } from "@/lib/adminError";
import { useConfig } from "@/lib/config";
import { isMissingProcessorJobError, processor } from "@/lib/processor";
import { Button, PageHeader, Empty } from "@/components/ui";
import IngestionJobFailurePanel from "@/components/IngestionJobFailurePanel";
import ManualMetadataModal from "@/components/ManualMetadataModal";
import {
  filterSupersededIngestionJobs,
  formatIngestionJobLabel,
  formatMetadataDisplay,
  isAwaitingMetadata,
  isIngestionFailed,
  isLocalProcessorJob,
  markIngestionJobRetrying,
  replaceRetriedIngestionJob,
  type IngestionJobStatus,
  updateIngestionJobInList,
} from "@/lib/ingestionJob";
import {
  getJobMetadata,
  metadataResponseToJson,
  submitManualMetadata,
} from "@/lib/metadataApi";
import { cn, formatLocalDateTime, parseUtcIso } from "@/lib/utils";
import { useResumeAfterMetadata } from "@/hooks/useResumeAfterMetadata";

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
  awaitingmetadata: "bg-warning/15 text-warning",
};

function StatusBadge({ status }: { status: string }) {
  const cls = STATUS_COLOR[status.toLowerCase()] ?? "bg-secondary text-muted";
  return (
    <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-medium", cls)}>
      {status}
    </span>
  );
}

/**
 * Compact inline metadata preview shown beneath the status badge in a job row.
 *
 * - Shows "Honda CBR600RR (2023) — sport" (or partial) when parsed metadata exists.
 * - Adds a fill-rate badge: warning style below 50%, muted percentage at 50–99%.
 * - Renders nothing when the job has no metadata or is fully complete.
 */
function JobMetadataInline({ job }: { job: IngestionJobStatus }) {
  const metadataDisplay = formatMetadataDisplay(job);

  // Nothing to show when there is no parsed metadata at all.
  if (metadataDisplay === null) return null;

  const fillRate = job.fillRate;
  const isComplete = job.isComplete === true;

  // Fill-rate badge logic (omitted when fully complete).
  let fillBadge: ReactNode = null;
  if (typeof fillRate === "number" && Number.isFinite(fillRate) && !isComplete) {
    const pct = Math.round(fillRate * 100);
    if (fillRate < 0.5) {
      fillBadge = (
        <span className="ml-1.5 rounded bg-danger/15 px-1.5 py-0.5 text-[10px] font-medium text-danger">
          {pct}% complete
        </span>
      );
    } else if (fillRate < 1.0) {
      fillBadge = (
        <span className="ml-1.5 rounded bg-warning/15 px-1.5 py-0.5 text-[10px] font-medium text-warning">
          {pct}% complete
        </span>
      );
    }
  }

  return (
    <div className="mt-1 leading-tight">
      <span className="text-xs text-neutral-500">{metadataDisplay}</span>
      {fillBadge}
    </div>
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

function isActiveJobStatus(status: string) {
  return ["queued", "pending", "processing", "running", "inprogress"].includes(
    status.toLowerCase(),
  );
}

async function sleep(ms: number) {
  await new Promise((resolve) => window.setTimeout(resolve, ms));
}

export default function JobsScreen() {
  const qc = useQueryClient();
  const apiBaseUrl = useConfig((state) => state.config.apiBaseUrl);
  const localProcessorPort = useConfig((state) => state.config.localProcessorPort);
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

  async function stopLocalProcessorRun(job: IngestionJobStatus) {
    if (!isActiveJobStatus(job.status) || !isLocalProcessorJob(job) || !job.docIngestionRunId) {
      return;
    }

    const isListening = await processor.isListening(localProcessorPort);
    if (!isListening) {
      return;
    }

    try {
      await processor.stopJob(job.docIngestionRunId, localProcessorPort);
    } catch (error) {
      if (isMissingProcessorJobError(error)) {
        console.warn(
          "Per-job local processor stop skipped because the run is no longer present:",
          job.docIngestionRunId,
        );
        return;
      }

      throw new Error(
        formatAdminError(error, {
          action: `Stopping local processor run ${job.docIngestionRunId}`,
          kind: "local-processor",
          localProcessorPort,
          location: "endpoint",
        }),
      );
    }
  }

  async function waitForDeleteReady(jobId: string) {
    const deadline = Date.now() + 10_000;
    while (Date.now() < deadline) {
      const status = (await api.get<IngestionJobStatus>(`/api/ingestion/jobs/${jobId}`)).data;
      if (!isActiveJobStatus(status.status)) {
        return;
      }

      await sleep(250);
    }

    throw new Error(`Timed out waiting for ingestion job ${jobId} to stop before deletion.`);
  }

  async function prepareJobForDeletion(job: IngestionJobStatus) {
    if (!isActiveJobStatus(job.status)) {
      return;
    }

    await stopLocalProcessorRun(job);
    await api.post(`/api/ingestion/jobs/${job.jobId}/cancel`);
    await waitForDeleteReady(job.jobId);
  }

  const remove = useMutation({
    mutationFn: async (job: IngestionJobStatus) => {
      await prepareJobForDeletion(job);
      await api.delete(`/api/ingestion/jobs/${job.jobId}`);
      return job.jobId;
    },
    onSuccess: (jobId) => {
      setDeleteError(null);
      qc.setQueriesData<IngestionJobStatus[]>({ queryKey: ["jobs"] }, (old) =>
        old?.filter((job) => job.jobId !== jobId),
      );
      qc.setQueryData<IngestionJobStatus[]>(["ingestion", "upload-jobs"], (old) =>
        old?.filter((job) => job.jobId !== jobId),
      );
      void qc.invalidateQueries({ queryKey: ["jobs"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
    },
    onError: (err: unknown) => {
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        setDeleteError(err.response.data?.detail ?? "Delete is already in progress for this job.");
        return;
      }
      setDeleteError(err instanceof Error ? err.message : formatAdminError(err, {
        action: "Deleting ingestion job",
        kind: "cloud-api",
        apiBaseUrl,
      }));
    },
  });

  const jobList = filterSupersededIngestionJobs(jobs.data, supersededRetryJobIds);
  const active = jobList.filter((j) =>
    ["processing", "queued", "pending"].includes(j.status.toLowerCase())
  ).length;

  // --- Manual metadata flow (Phase 4) ---
  // The first job in an awaiting-metadata state drives auto-open of the modal.
  // `closedMetadataJobId` remembers a job the admin dismissed (or just submitted) so the
  // modal does not re-open on the next 15s poll. A different job id clears the dismissal
  // automatically.
  //
  // `selectedMetadataJobId` holds an explicitly-chosen job when the admin clicks the
  // "Enter Metadata" button on a specific row. This takes priority over the auto-detected
  // first awaiting job, allowing the admin to re-open a dismissed modal or pick among
  // multiple awaiting-metadata jobs.
  const awaitingMetadataJob = useMemo(
    () => jobList.find((j) => isAwaitingMetadata(j)),
    [jobList],
  );
  const [closedMetadataJobId, setClosedMetadataJobId] = useState<string | null>(null);
  const [selectedMetadataJobId, setSelectedMetadataJobId] = useState<string | null>(null);
  const [metadataSuccess, setMetadataSuccess] = useState<string | null>(null);

  // --- Resume-after-metadata flow (shared hook) ---
  // After metadata submission, the C# API transitions the job to Processing/resuming.
  // The shared hook stores the submitted metadata and triggers the Python processor
  // when it detects the "resuming" stage via polling.
  const { storeMetadataForResume, resumeError, clearResumeError } =
    useResumeAfterMetadata({ jobs: jobs.data, localProcessorPort });

  // The job the modal is about: manual selection takes priority (as long as it is still
  // awaiting metadata); otherwise fall back to the first auto-detected awaiting job.
  const metadataJob = useMemo(() => {
    if (selectedMetadataJobId) {
      const selected = jobList.find(
        (j) => j.jobId === selectedMetadataJobId && isAwaitingMetadata(j),
      );
      if (selected) return selected;
    }
    return awaitingMetadataJob;
  }, [jobList, selectedMetadataJobId, awaitingMetadataJob]);

  const showMetadataModal = !!metadataJob && metadataJob.jobId !== closedMetadataJobId;

  // Clears the dismissal and marks a specific job as selected so the modal opens for it.
  const handleOpenMetadataModal = useCallback((job: IngestionJobStatus) => {
    setClosedMetadataJobId(null);
    setSelectedMetadataJobId(job.jobId);
  }, []);

  const metadataQuery = useQuery({
    queryKey: ["ingestion", "job-metadata", metadataJob?.jobId],
    queryFn: () => getJobMetadata(metadataJob!.jobId),
    enabled: showMetadataModal,
    refetchOnWindowFocus: false,
    retry: 1,
  });

  const initialMetadata = useMemo(
    () => (metadataQuery.data ? metadataResponseToJson(metadataQuery.data) : undefined),
    [metadataQuery.data],
  );

  const handleSubmitMetadata = useCallback(
    async (metadataJson: string) => {
      const job = metadataJob;
      if (!job) throw new Error("No job is awaiting metadata.");

      try {
        await submitManualMetadata(job.jobId, metadataJson);
      } catch (err) {
        // Re-throw a formatted message so the modal can display it inline and stay open.
        throw new Error(
          formatAdminError(err, {
            action: "Submitting manual metadata",
            kind: "cloud-api",
            apiBaseUrl,
          }),
        );
      }

      // Store the metadata for the resume flow. The shared hook's useEffect watching
      // for "resuming" stage will detect the state change and call the Python processor.
      storeMetadataForResume(job.jobId, metadataJson);

      // Success: close the modal immediately, notify the user, and refresh the jobs list.
      setClosedMetadataJobId(job.jobId);
      setSelectedMetadataJobId(null);
      setMetadataSuccess("Metadata submitted. Pipeline resuming.");
      void qc.invalidateQueries({ queryKey: ["jobs"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "upload-jobs"] });
      void qc.invalidateQueries({ queryKey: ["ingestion", "job-metadata"] });
    },
    [metadataJob, apiBaseUrl, qc, storeMetadataForResume],
  );

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

      {metadataSuccess && (
        <div className="mb-4 flex items-center gap-2 rounded-md border border-success/40 bg-success/10 px-3 py-2 text-sm text-success">
          <CheckCircle2 className="h-4 w-4 shrink-0" />
          <span className="flex-1">{metadataSuccess}</span>
          <button
            className="shrink-0 rounded p-0.5 text-success hover:bg-success/20"
            onClick={() => setMetadataSuccess(null)}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}

      {resumeError && (
        <div className="mb-4 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          <span className="flex-1">{resumeError}</span>
          <button
            className="shrink-0 rounded p-0.5 text-danger hover:bg-danger/20"
            onClick={clearResumeError}
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
          <Empty>
            Could not load jobs: {formatAdminError(jobs.error, {
              action: "Loading ingestion jobs",
              kind: "cloud-api",
              apiBaseUrl,
            })}
          </Empty>
        ) : jobList.length === 0 ? (
          <Empty>No jobs found.</Empty>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border text-left text-xs text-muted">
                <th className="px-4 py-2.5 font-medium">Job</th>
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
                      <div className="truncate">{formatIngestionJobLabel(j)}</div>
                      {j.sourceFileName && (
                        <div className="truncate text-xs text-muted" title={j.sourceFileName}>
                          {j.sourceFileName}
                        </div>
                      )}
                      {(isIngestionFailed(j.status) || isAwaitingMetadata(j)) && (
                        <IngestionJobFailurePanel
                          job={j}
                          onEnterMetadata={
                            isAwaitingMetadata(j)
                              ? () => handleOpenMetadataModal(j)
                              : undefined
                          }
                        />
                      )}
                    </td>
                    <td className="px-4 py-3 align-top">
                      <StatusBadge status={j.status} />
                      <JobMetadataInline job={j} />
                    </td>
                    <td
                      className="px-4 py-3 align-top text-xs text-muted"
                      title={formatLocalDateTime(j.createdAtUtc)}
                    >
                      {relativeTime(j.createdAtUtc)}
                    </td>
                    <td className="px-4 py-3 align-top">
                      <div className="flex justify-end gap-1">
                        {isAwaitingMetadata(j) && (
                          <button
                            title="Enter Metadata"
                            aria-label="Enter Metadata"
                            onClick={() => handleOpenMetadataModal(j)}
                            className="rounded p-1 text-muted hover:text-warning"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                        )}
                        {isIngestionFailed(j.status) && (
                          <button
                            title="Retry"
                            aria-label="Retry"
                            disabled={retry.isPending}
                            onClick={() => retry.mutate(j.jobId)}
                            className="rounded p-1 text-muted hover:text-primary disabled:opacity-50"
                          >
                            <RotateCcw className="h-4 w-4" />
                          </button>
                        )}
                        <button
                          title="Delete"
                          aria-label="Delete"
                          disabled={remove.isPending}
                          onClick={() => remove.mutate(j)}
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

      {metadataJob && (
        <ManualMetadataModal
          isOpen={showMetadataModal}
          jobId={metadataJob.jobId}
          initialMetadata={initialMetadata}
          onSubmit={handleSubmitMetadata}
          onClose={() => {
            setClosedMetadataJobId(metadataJob.jobId);
            setSelectedMetadataJobId(null);
          }}
        />
      )}
    </div>
  );
}
