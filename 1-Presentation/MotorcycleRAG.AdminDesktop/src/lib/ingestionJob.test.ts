import { describe, expect, it } from "vitest";
import {
  filterSupersededIngestionJobs,
  formatIngestionJobLabel,
  isAwaitingMetadata,
  replaceRetriedIngestionJob,
  type IngestionJobStatus,
} from "./ingestionJob";

function job(jobId: string, status: string): IngestionJobStatus {
  return {
    jobId,
    status,
    createdAtUtc: "2026-07-03T17:40:01+00:00",
    inputType: "manual-pdf",
    inputRef: `${jobId}.pdf`,
  };
}

describe("replaceRetriedIngestionJob", () => {
  it("replaces the original row when retry returns the same job id", () => {
    const original = job("job-1", "failed");
    const retried = job("job-1", "queued");

    expect(replaceRetriedIngestionJob([original, job("job-2", "completed")], "job-1", retried)).toEqual([
      retried,
      job("job-2", "completed"),
    ]);
  });

  it("removes the original failed row and inserts the new retry job first", () => {
    const retried = job("job-3", "queued");

    expect(replaceRetriedIngestionJob([job("job-1", "failed"), job("job-2", "completed")], "job-1", retried)).toEqual([
      retried,
      job("job-2", "completed"),
    ]);
  });

  it("deduplicates an already cached retry job", () => {
    const retried = job("job-3", "processing");

    expect(replaceRetriedIngestionJob([job("job-1", "failed"), job("job-3", "queued")], "job-1", retried)).toEqual([
      retried,
    ]);
  });
});

describe("filterSupersededIngestionJobs", () => {
  it("filters superseded retry source jobs", () => {
    expect(
      filterSupersededIngestionJobs(
        [job("job-1", "failed"), job("job-3", "processing")],
        new Set(["job-1"]),
      ),
    ).toEqual([job("job-3", "processing")]);
  });
});

describe("formatIngestionJobLabel", () => {
  it("uses the SQL identity when available", () => {
    expect(formatIngestionJobLabel({ id: 22, jobId: "guid-22" } as IngestionJobStatus)).toBe(
      "Job 22",
    );
  });

  it("falls back to the guid when no SQL identity exists", () => {
    expect(formatIngestionJobLabel({ jobId: "guid-22" } as IngestionJobStatus)).toBe("guid-22");
  });
});

describe("isAwaitingMetadata", () => {
  it("is true when the C# status is AwaitingMetadata", () => {
    expect(isAwaitingMetadata({ status: "AwaitingMetadata", requiresManualMetadata: undefined })).toBe(true);
  });

  it("is true regardless of status casing", () => {
    expect(isAwaitingMetadata({ status: "awaitingmetadata", requiresManualMetadata: undefined })).toBe(true);
  });

  it("is true when the backend set the requiresManualMetadata flag", () => {
    expect(isAwaitingMetadata({ status: "Processing", requiresManualMetadata: true })).toBe(true);
  });

  it("is false for ordinary in-progress jobs", () => {
    expect(isAwaitingMetadata({ status: "Processing", requiresManualMetadata: false })).toBe(false);
  });

  it("is false for completed jobs", () => {
    expect(isAwaitingMetadata({ status: "Completed", requiresManualMetadata: false })).toBe(false);
  });
});
