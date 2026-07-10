import { describe, expect, it } from "vitest";
import {
  filterSupersededIngestionJobs,
  formatIngestionJobLabel,
  formatMetadataDisplay,
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

describe("formatMetadataDisplay", () => {
  it("returns null when no metadata fields are present", () => {
    expect(formatMetadataDisplay({})).toBeNull();
  });

  it("returns null when all fields are empty/whitespace", () => {
    expect(
      formatMetadataDisplay({ make: "  ", model: "", category: "   " }),
    ).toBeNull();
  });

  it("combines make and model into one segment", () => {
    expect(
      formatMetadataDisplay({ make: "Honda", model: "CBR600RR" }),
    ).toBe("Honda CBR600RR");
  });

  it("includes the year in parentheses", () => {
    expect(
      formatMetadataDisplay({ make: "Honda", model: "CBR600RR", year: 2023 }),
    ).toBe("Honda CBR600RR (2023)");
  });

  it("appends the category after an em-dash", () => {
    expect(
      formatMetadataDisplay({
        make: "Honda",
        model: "CBR600RR",
        year: 2023,
        category: "sport",
      }),
    ).toBe("Honda CBR600RR (2023) — sport");
  });

  it("shows only make when only make is present", () => {
    expect(formatMetadataDisplay({ make: "Honda" })).toBe("Honda");
  });

  it("shows only model when only model is present", () => {
    expect(formatMetadataDisplay({ model: "CBR600RR" })).toBe("CBR600RR");
  });

  it("shows only year when only year is present", () => {
    expect(formatMetadataDisplay({ year: 2023 })).toBe("(2023)");
  });

  it("shows only category when only category is present", () => {
    expect(formatMetadataDisplay({ category: "sport" })).toBe("— sport");
  });

  it("trims whitespace from make/model/category", () => {
    expect(
      formatMetadataDisplay({ make: "  Honda  ", model: " CBR600RR ", category: " sport " }),
    ).toBe("Honda CBR600RR — sport");
  });

  it("ignores non-finite year values", () => {
    expect(
      formatMetadataDisplay({ make: "Honda", model: "CBR600RR", year: Number.NaN }),
    ).toBe("Honda CBR600RR");
  });
});
