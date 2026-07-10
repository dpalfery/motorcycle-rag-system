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
  /** Original source file name (basename only) for display purposes. */
  sourceFileName?: string;
  /** Extracted/submitted metadata blob, surfaced for display when a job is awaiting manual entry. */
  metadata?: Record<string, unknown>;
  /** True when the backend has determined this job requires manual metadata entry. */
  requiresManualMetadata?: boolean;

  // --- Parsed motorcycle metadata (populated from job.MetadataJson when present) ---

  /** Manufacturer, e.g. "Honda". Null when no metadata is present. */
  make?: string;
  /** Model name, e.g. "CBR600RR". Null when no metadata is present. */
  model?: string;
  /** Model year, e.g. 2023. Null when no metadata is present. */
  year?: number;
  /** Motorcycle category, e.g. "sport". Null when no metadata is present. */
  category?: string;
  /** Free-form tags extracted from the document. Null when no metadata is present. */
  tags?: string[];
  /** Fraction (0.0–1.0) of the four required fields populated. Null when no metadata is present. */
  fillRate?: number;
  /** True when all four required fields are populated; false otherwise. Null when no metadata is present. */
  isComplete?: boolean;
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
 * machine has transitioned the job to `AwaitingMetadata`, or when the backend has set the
 * structured `requiresManualMetadata` flag on the status response.
 */
export function isAwaitingMetadata(
  job: Pick<IngestionJobStatus, "status" | "requiresManualMetadata">,
): boolean {
  return (
    job.status?.toLowerCase() === "awaitingmetadata" ||
    job.requiresManualMetadata === true
  );
}

/**
 * True when a job has transitioned from AwaitingMetadata to Processing with the
 * "resuming" stage. The C# API sets currentStage="resuming" after manual metadata
 * submission; the Admin Desktop must detect this and call the Python processor to
 * actually resume processing.
 */
export function isResumingAfterMetadata(
  job: Pick<IngestionJobStatus, "status" | "currentStage">,
): boolean {
  return (
    job.status?.toLowerCase() === "processing" &&
    job.currentStage === "resuming"
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

/**
 * Builds a compact, one-line metadata summary for inline display in a job row.
 *
 * Returns `null` when the job has no parsed metadata (i.e. none of make/model/year/category
 * are present). Tags are deliberately excluded from the inline display — they are too verbose
 * for a table cell and are surfaced elsewhere (the metadata modal).
 *
 * Examples:
 *   all fields  → "Honda CBR600RR (2023) — sport"
 *   make+model  → "Honda CBR600RR"
 *   year only   → "2023"
 */
export function formatMetadataDisplay(
  job: Pick<IngestionJobStatus, "make" | "model" | "year" | "category">,
): string | null {
  const parts: string[] = [];

  const make = job.make?.trim();
  const model = job.model?.trim();
  const year = job.year;
  const category = job.category?.trim();

  // Combine make + model into a single segment, e.g. "Honda CBR600RR".
  const makeModel = [make, model].filter(Boolean).join(" ");
  if (makeModel) parts.push(makeModel);

  if (typeof year === "number" && Number.isFinite(year)) {
    parts.push(`(${year})`);
  }

  if (category) parts.push(`— ${category}`);

  return parts.length > 0 ? parts.join(" ") : null;
}
