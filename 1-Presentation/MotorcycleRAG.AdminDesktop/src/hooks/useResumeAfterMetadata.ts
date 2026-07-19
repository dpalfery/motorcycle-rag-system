import { useCallback, useEffect, useRef, useState } from "react";
import { processor, type ProcessorMetadata } from "@/lib/processor";
import {
  isResumingAfterMetadata,
  type IngestionJobStatus,
} from "@/lib/ingestionJob";

/**
 * Shared hook for the "resume after metadata" orchestration flow.
 *
 * After an admin submits manual metadata, the C# API transitions the job to
 * `Processing` with `currentStage="resuming"`. Both JobsScreen and ProcessorScreen
 * poll the cloud jobs list and must detect this state transition to call the
 * Python processor's `/process/pdf` endpoint with the submitted metadata.
 *
 * Previously this logic was duplicated in both screens with per-component refs,
 * which risked double-resume when navigating between screens. This hook
 * centralises the refs and the effect so both screens share one orchestration
 * instance per mount.
 */

/** Terminal statuses that should trigger ref cleanup. */
const TERMINAL_STATUSES = new Set([
  "completed",
  "complete",
  "done",
  "succeeded",
  "failed",
  "error",
  "cancelled",
]);

/**
 * Parse the stored metadata JSON into the shape the Python processor expects.
 * Returns `null` on parse failure so the caller can skip the resume call and
 * surface an error rather than sending empty metadata.
 *
 * Exported for testability; screens should not call this directly — the hook
 * uses it internally when triggering the resume.
 */
export function parseMetadataJsonForProcessor(
  metadataJson: string,
): ProcessorMetadata | null {
  try {
    const parsed = JSON.parse(metadataJson) as Record<string, unknown>;
    return {
      make: typeof parsed.make === "string" ? parsed.make : null,
      model: typeof parsed.model === "string" ? parsed.model : null,
      year: typeof parsed.year === "number" ? parsed.year : null,
      document_type:
        typeof parsed.document_type === "string" ? parsed.document_type : null,
      language: typeof parsed.language === "string" ? parsed.language : "en",
      custom: {
        category: parsed.category ?? null,
        tags: Array.isArray(parsed.tags) ? parsed.tags : [],
      },
    };
  } catch {
    return null;
  }
}

export interface UseResumeAfterMetadataOptions {
  /** The current cloud jobs list (from useQuery). */
  jobs: IngestionJobStatus[] | undefined;
  /** Port the local Python processor is listening on. */
  localProcessorPort: number;
}

export interface UseResumeAfterMetadataResult {
  /**
   * Store metadata JSON for a job. Call this after a successful metadata
   * submission so the hook can resume the processor when the job transitions
   * to the "resuming" stage.
   */
  storeMetadataForResume: (jobId: string, metadataJson: string) => void;
  /**
   * User-facing error message when a resume fails (e.g., metadata parse error).
   * The consuming screen should render this in a dismissible banner.
   */
  resumeError: string | null;
  /** Clear the resume error banner. */
  clearResumeError: () => void;
}

export function useResumeAfterMetadata({
  jobs,
  localProcessorPort,
}: UseResumeAfterMetadataOptions): UseResumeAfterMetadataResult {
  const pendingResumeMetadata = useRef<Map<string, string>>(new Map());
  const resumedJobIds = useRef<Set<string>>(new Set());
  const [resumeError, setResumeError] = useState<string | null>(null);

  const storeMetadataForResume = useCallback(
    (jobId: string, metadataJson: string) => {
      pendingResumeMetadata.current.set(jobId, metadataJson);
    },
    [],
  );

  const clearResumeError = useCallback(() => setResumeError(null), []);

  // Finding 5: Evict entries for jobs that reached terminal status or
  // disappeared from the API response. Prevents unbounded ref growth.
  useEffect(() => {
    if (!jobs) return;

    const activeJobIds = new Set(jobs.map((j) => j.jobId));

    for (const jobId of resumedJobIds.current) {
      if (!activeJobIds.has(jobId)) {
        resumedJobIds.current.delete(jobId);
      }
    }
    for (const jobId of pendingResumeMetadata.current.keys()) {
      if (!activeJobIds.has(jobId)) {
        pendingResumeMetadata.current.delete(jobId);
      }
    }

    // Also clean up jobs that reached a terminal status.
    for (const job of jobs) {
      if (TERMINAL_STATUSES.has(job.status.toLowerCase())) {
        resumedJobIds.current.delete(job.jobId);
        pendingResumeMetadata.current.delete(job.jobId);
      }
    }
  }, [jobs]);

  // Finding 1 & 2: Detect "resuming" stage and trigger processor.
  // If metadata JSON fails to parse, skip the resume and surface an error
  // rather than sending empty metadata.
  useEffect(() => {
    if (!jobs) return;

    for (const job of jobs) {
      if (!isResumingAfterMetadata(job)) continue;
      if (resumedJobIds.current.has(job.jobId)) continue;
      if (!job.docIngestionRunId) continue;

      const metadataJson = pendingResumeMetadata.current.get(job.jobId);
      if (!metadataJson) continue;

      const metadata = parseMetadataJsonForProcessor(metadataJson);
      if (!metadata) {
        // Parse failed — skip resume and surface error.
        console.error(
          `Failed to parse metadata JSON for job ${job.jobId}; skipping resume.`,
        );
        setResumeError(
          `Metadata for job ${job.jobId} could not be parsed. Resume skipped. Please re-enter metadata.`,
        );
        // Clean up so we don't retry with bad data on the next poll.
        pendingResumeMetadata.current.delete(job.jobId);
        resumedJobIds.current.add(job.jobId);
        continue;
      }

      // Mark as resumed immediately to prevent duplicate calls on re-renders.
      resumedJobIds.current.add(job.jobId);
      pendingResumeMetadata.current.delete(job.jobId);

      const payload = {
        upload_id: job.inputRef,
        document_type: job.inputType,
        // Hard-coded container name: the C# service reads this from configuration,
        // but the React client does not have access to that config. "raw-uploads"
        // is the established convention for all ingestion uploads.
        blob_container: "raw-uploads",
        job_id: job.docIngestionRunId,
        metadata,
      };

      processor
        .resumeProcessPdf(payload, localProcessorPort)
        .then(() => {
          console.info(`Processor resume triggered for job ${job.jobId}`);
        })
        .catch((err) => {
          console.error(
            "Failed to resume processor for job",
            job.jobId,
            err instanceof Error ? err.message : err,
          );
          // Allow retry on next poll if the resume failed.
          resumedJobIds.current.delete(job.jobId);
          pendingResumeMetadata.current.set(job.jobId, metadataJson);
        });
    }
  }, [jobs, localProcessorPort]);

  return { storeMetadataForResume, resumeError, clearResumeError };
}
