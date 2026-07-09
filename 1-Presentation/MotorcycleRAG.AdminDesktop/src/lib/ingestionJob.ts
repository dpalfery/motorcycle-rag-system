import type { IngestionCoverageMetrics, IngestionWorkloadLimits } from "./ingestionJobTypes";

export interface IngestionJobStatus {
  id?: number;
  jobId: string;
  status: string;
  createdAtUtc: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
  currentStage?: string;
  stageSetAtUtc?: string;
  inputType: string;
  inputRef: string;
  computeProvider?: string;
  manualDocumentId?: string;
  totalPages?: number;
  pagesCapturedViewableCount?: number;
  pagesWithSearchableTextCount?: number;
  pagesWithOcrTextCount?: number;
  pagesWithNativeTextCount?: number;
  missingPages?: number[];
  coverage?: IngestionCoverageMetrics;
  workloadLimits?: IngestionWorkloadLimits;
  message?: string;
  failureReason?: string;
  failureDetail?: string;
  docIngestionRunId?: string;
  expectedChunkCount?: number;
  indexedChunkCount?: number;
  statusUrl?: string;
  /** Extracted/submitted metadata blob, surfaced for display when a job is awaiting manual entry. */
  metadata?: Record<string, unknown>;
}

export function formatIngestionJobLabel(job: Pick<IngestionJobStatus, "id" | "jobId">): string {
  return typeof job.id === "number" && Number.isFinite(job.id) ? `Job ${job.id}` : job.jobId;
}

export const INGESTION_FAILED_STATUSES = ["failed", "error", "cancelled"] as const;

export function isIngestionFailed(status: string): boolean {
  return INGESTION_FAILED_STATUSES.includes(
    status.toLowerCase() as (typeof INGESTION_FAILED_STATUSES)[number],
  );
}

/**
 * True when a job has paused for manual metadata entry. This is the case when the C# state
 * machine has transitioned the job to `AwaitingMetadata`, or when the Python processor has
 * reported the `needs-manual-metadata` stage (which the API mirrors into the awaiting state).
 */
export function isAwaitingMetadata(
  job: Pick<IngestionJobStatus, "status" | "currentStage">,
): boolean {
  return (
    job.status?.toLowerCase() === "awaitingmetadata" ||
    job.currentStage === "needs-manual-metadata"
  );
}

export function markIngestionJobRetrying(
  job: IngestionJobStatus,
  retryStatus: string = "queued",
): IngestionJobStatus {
  return {
    ...job,
    status: retryStatus,
    message: undefined,
    failureReason: undefined,
    failureDetail: undefined,
    completedAtUtc: undefined,
  };
}

export function updateIngestionJobInList(
  jobs: IngestionJobStatus[] | undefined,
  jobId: string,
  update: (job: IngestionJobStatus) => IngestionJobStatus,
) {
  return jobs?.map((job) => (job.jobId === jobId ? update(job) : job));
}

export function replaceRetriedIngestionJob(
  jobs: IngestionJobStatus[] | undefined,
  originalJobId: string,
  retriedJob: IngestionJobStatus,
) {
  if (!jobs) return jobs;

  if (retriedJob.jobId === originalJobId) {
    return jobs.map((job) => (job.jobId === originalJobId ? retriedJob : job));
  }

  return [
    retriedJob,
    ...jobs.filter(
      (job) => job.jobId !== originalJobId && job.jobId !== retriedJob.jobId,
    ),
  ];
}

export function filterSupersededIngestionJobs(
  jobs: IngestionJobStatus[] | undefined,
  supersededJobIds: ReadonlySet<string>,
) {
  if (!jobs || supersededJobIds.size === 0) return jobs ?? [];
  return jobs.filter((job) => !supersededJobIds.has(job.jobId));
}

export function isLocalProcessorJob(job: Pick<IngestionJobStatus, "computeProvider" | "docIngestionRunId">): boolean {
  if (!job.docIngestionRunId) return false;
  const provider = (job.computeProvider ?? "").toLowerCase();
  return provider.includes("local");
}

export interface ProcessorJobDetail {
  job_id?: string;
  status?: string;
  message?: string;
  error?: string;
  progress?: number;
  document_type?: string;
  created_at?: string;
  updated_at?: string;
  completion_time?: string;
  pages_processed?: number;
  total_pages?: number;
  chunks_processed?: number;
}

export function formatDiagnosticValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  return JSON.stringify(value, null, 2);
}
