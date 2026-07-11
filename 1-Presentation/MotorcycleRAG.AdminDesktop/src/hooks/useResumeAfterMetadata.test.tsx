import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { IngestionJobStatus } from "@/lib/ingestionJob";
import {
  parseMetadataJsonForProcessor,
  useResumeAfterMetadata,
} from "./useResumeAfterMetadata";

const { resumeProcessPdf } = vi.hoisted(() => ({
  resumeProcessPdf: vi.fn(),
}));

vi.mock("@/lib/processor", () => ({
  processor: { resumeProcessPdf },
}));

function makeJob(
  overrides: Partial<IngestionJobStatus> = {},
): IngestionJobStatus {
  return {
    id: 1,
    jobId: "job-1",
    status: "Processing",
    currentStage: "resuming",
    createdAtUtc: "2026-07-10T12:00:00Z",
    inputType: "manual-pdf",
    inputRef: "uploads/manual.pdf",
    docIngestionRunId: "run-1",
    ...overrides,
  };
}

describe("parseMetadataJsonForProcessor", () => {
  it("maps supported metadata and custom fields", () => {
    expect(
      parseMetadataJsonForProcessor(
        JSON.stringify({
          make: "Honda",
          model: "CBR600RR",
          year: 2023,
          document_type: "service-manual",
          language: "fr",
          category: "sport",
          tags: ["engine", "brakes"],
        }),
      ),
    ).toEqual({
      make: "Honda",
      model: "CBR600RR",
      year: 2023,
      document_type: "service-manual",
      language: "fr",
      custom: { category: "sport", tags: ["engine", "brakes"] },
    });
  });

  it("normalizes invalid optional values and rejects malformed JSON", () => {
    expect(
      parseMetadataJsonForProcessor(
        JSON.stringify({
          make: 1,
          model: false,
          year: "2023",
          document_type: [],
          language: null,
          tags: "engine",
        }),
      ),
    ).toEqual({
      make: null,
      model: null,
      year: null,
      document_type: null,
      language: "en",
      custom: { category: null, tags: [] },
    });
    expect(parseMetadataJsonForProcessor("{not-json")).toBeNull();
  });
});

describe("useResumeAfterMetadata", () => {
  beforeEach(() => {
    resumeProcessPdf.mockReset().mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("resumes a matching job once with parsed metadata", async () => {
    const info = vi.spyOn(console, "info").mockImplementation(() => undefined);
    const initialProps = {
      jobs: [makeJob({ status: "Queued", currentStage: "queued" })],
      localProcessorPort: 8123,
    };
    const { result, rerender } = renderHook(
      (props: typeof initialProps) => useResumeAfterMetadata(props),
      { initialProps },
    );

    act(() => {
      result.current.storeMetadataForResume(
        "job-1",
        JSON.stringify({ make: "Honda", category: "manual", tags: ["pdf"] }),
      );
    });
    rerender({ ...initialProps, jobs: [makeJob()] });

    await waitFor(() => expect(resumeProcessPdf).toHaveBeenCalledOnce());
    expect(resumeProcessPdf).toHaveBeenCalledWith(
      {
        upload_id: "uploads/manual.pdf",
        document_type: "manual-pdf",
        blob_container: "raw-uploads",
        job_id: "run-1",
        metadata: {
          make: "Honda",
          model: null,
          year: null,
          document_type: null,
          language: "en",
          custom: { category: "manual", tags: ["pdf"] },
        },
      },
      8123,
    );
    await waitFor(() =>
      expect(info).toHaveBeenCalledWith(
        "Processor resume triggered for job job-1",
      ),
    );

    rerender({ ...initialProps, jobs: [makeJob({ inputRef: "changed.pdf" })] });
    expect(resumeProcessPdf).toHaveBeenCalledOnce();
  });

  it("surfaces malformed metadata and allows the error to be cleared", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const waitingJob = makeJob({ status: "Queued", currentStage: "queued" });
    const { result, rerender } = renderHook(
      ({ jobs }) =>
        useResumeAfterMetadata({ jobs, localProcessorPort: 8100 }),
      { initialProps: { jobs: [waitingJob] } },
    );

    act(() => result.current.storeMetadataForResume("job-1", "{broken"));
    rerender({ jobs: [makeJob()] });

    await waitFor(() =>
      expect(result.current.resumeError).toContain(
        "Metadata for job job-1 could not be parsed",
      ),
    );
    expect(resumeProcessPdf).not.toHaveBeenCalled();

    act(() => result.current.clearResumeError());
    expect(result.current.resumeError).toBeNull();
  });

  it("retries on the next poll after the processor rejects the resume", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    resumeProcessPdf
      .mockRejectedValueOnce(new Error("processor unavailable"))
      .mockResolvedValueOnce(undefined);
    const waiting = makeJob({ status: "Queued", currentStage: "queued" });
    const { result, rerender } = renderHook(
      ({ jobs }) =>
        useResumeAfterMetadata({ jobs, localProcessorPort: 8100 }),
      { initialProps: { jobs: [waiting] } },
    );

    act(() =>
      result.current.storeMetadataForResume("job-1", JSON.stringify({})),
    );
    rerender({ jobs: [makeJob()] });
    await waitFor(() => expect(resumeProcessPdf).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(error).toHaveBeenCalledWith(
        "Failed to resume processor for job job-1:",
        "processor unavailable",
      ),
    );

    rerender({
      jobs: [makeJob({ currentStage: "processing" })],
    });
    rerender({ jobs: [makeJob()] });
    await waitFor(() => expect(resumeProcessPdf).toHaveBeenCalledTimes(2));
  });

  it("ignores ineligible jobs and cleans pending entries for removed jobs", () => {
    const waiting = makeJob({ status: "Queued", currentStage: "queued" });
    const { result, rerender } = renderHook(
      ({ jobs }: { jobs: IngestionJobStatus[] | undefined }) =>
        useResumeAfterMetadata({ jobs, localProcessorPort: 8100 }),
      { initialProps: { jobs: undefined } },
    );

    act(() => {
      result.current.storeMetadataForResume("job-1", JSON.stringify({}));
      result.current.storeMetadataForResume("removed", JSON.stringify({}));
    });
    rerender({ jobs: [waiting] });
    rerender({
      jobs: [
        makeJob({ jobId: "no-run", docIngestionRunId: undefined }),
        makeJob({ jobId: "no-metadata" }),
      ],
    });
    rerender({ jobs: [makeJob({ jobId: "removed" })] });

    expect(resumeProcessPdf).not.toHaveBeenCalled();
  });

  it.each(["Completed", "complete", "Done", "Succeeded", "Failed", "Error", "Cancelled"])(
    "evicts pending metadata when a job becomes terminal: %s",
    (status) => {
      const waiting = makeJob({ status: "Queued", currentStage: "queued" });
      const { result, rerender } = renderHook(
        ({ jobs }) =>
          useResumeAfterMetadata({ jobs, localProcessorPort: 8100 }),
        { initialProps: { jobs: [waiting] } },
      );

      act(() =>
        result.current.storeMetadataForResume("job-1", JSON.stringify({})),
      );
      rerender({ jobs: [makeJob({ status, currentStage: "finished" })] });
      rerender({ jobs: [makeJob()] });

      expect(resumeProcessPdf).not.toHaveBeenCalled();
    },
  );
});
