import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/apiClient", () => ({
  api: {
    post: vi.fn(),
    get: vi.fn(),
  },
}));

// Imported after the apiClient mock is registered.
import { api } from "@/lib/apiClient";
import {
  getJobMetadata,
  metadataResponseToJson,
  submitManualMetadata,
  validateMetadataJson,
} from "./metadataApi";

function axiosError(status: number, detail: string) {
  return Object.assign(new Error(detail), {
    isAxiosError: true,
    response: { status, data: { detail } },
  });
}

describe("submitManualMetadata", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.clearAllMocks());

  it("POSTs the metadata JSON to the job metadata endpoint", async () => {
    vi.mocked(api.post).mockResolvedValueOnce({ status: 200, data: {} });

    await submitManualMetadata("job-123", '{"make":"Honda"}');

    expect(api.post).toHaveBeenCalledWith("/api/ingestion/jobs/job-123/metadata", {
      metadataJson: '{"make":"Honda"}',
    });
  });

  it("rejects when the API returns a non-2xx status", async () => {
    const err = axiosError(400, "Invalid JSON");
    vi.mocked(api.post).mockRejectedValueOnce(err);

    await expect(submitManualMetadata("job-1", "{bad")).rejects.toBe(err);
  });
});

describe("getJobMetadata", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.clearAllMocks());

  it("GETs and returns the metadata response body", async () => {
    const payload = {
      jobId: "job-1",
      make: "Honda",
      model: "CBR600RR",
      year: 2023,
      category: "sport",
      tags: ["inline-4"],
      fillRate: 1,
      isComplete: true,
      rawJson: '{"make":"Honda"}',
    };
    vi.mocked(api.get).mockResolvedValueOnce({ status: 200, data: payload });

    await expect(getJobMetadata("job-1")).resolves.toEqual(payload);
    expect(api.get).toHaveBeenCalledWith("/api/ingestion/jobs/job-1/metadata");
  });

  it("rejects on a 404 for an unknown job", async () => {
    const err = axiosError(404, "Not found");
    vi.mocked(api.get).mockRejectedValueOnce(err);

    await expect(getJobMetadata("missing")).rejects.toBe(err);
  });
});

describe("metadataResponseToJson", () => {
  it("prefers the server-provided rawJson", () => {
    expect(
      metadataResponseToJson({ jobId: "j1", rawJson: '  {"make":"Kawasaki"}  ' }),
    ).toBe('  {"make":"Kawasaki"}  ');
  });

  it("falls back to building JSON from individual fields", () => {
    expect(
      metadataResponseToJson({
        jobId: "j1",
        make: "Honda",
        model: "CBR600RR",
        year: 2023,
        category: "sport",
        tags: ["600cc"],
      }),
    ).toBe(JSON.stringify({ make: "Honda", model: "CBR600RR", year: 2023, category: "sport", tags: ["600cc"] }, null, 2));
  });

  it("includes all known fields (as null) when building the fallback JSON so the admin sees what the LLM detected", () => {
    expect(
      metadataResponseToJson({ jobId: "j1", make: "Yamaha", year: 0 }),
    ).toBe(JSON.stringify({ make: "Yamaha", model: null, year: null, category: null, tags: null }, null, 2));
  });
});

describe("validateMetadataJson", () => {
  it("accepts a complete object with the required fields", () => {
    const result = validateMetadataJson('{"make":"Honda","model":"CBR600RR","year":2023,"category":"sport"}');
    expect(result.ok).toBe(true);
    expect(result.value).toEqual({ make: "Honda", model: "CBR600RR", year: 2023, category: "sport" });
  });

  it("accepts trailing whitespace and an optional tags array", () => {
    const json = '   {"make":"Honda","model":"CBR600RR","year":2023,"category":"sport","tags":["600cc"]}   ';
    expect(validateMetadataJson(json).ok).toBe(true);
  });

  it("rejects an empty string", () => {
    const result = validateMetadataJson("   ");
    expect(result.ok).toBe(false);
    expect(result.error).toMatch(/required/i);
  });

  it("rejects malformed JSON", () => {
    const result = validateMetadataJson("{not json}");
    expect(result.ok).toBe(false);
    expect(result.error).toMatch(/valid json/i);
  });

  it("rejects a JSON array", () => {
    const result = validateMetadataJson('[1,2,3]');
    expect(result.ok).toBe(false);
    expect(result.error).toMatch(/single json object/i);
  });

  it("rejects a JSON primitive", () => {
    const result = validateMetadataJson('"hello"');
    expect(result.ok).toBe(false);
    expect(result.error).toMatch(/single json object/i);
  });

  it("rejects an object missing required fields", () => {
    const result = validateMetadataJson('{"make":"Honda","model":"CBR600RR"}');
    expect(result.ok).toBe(false);
    expect(result.error).toMatch(/year.*category|category.*year/i);
  });

  it("rejects when required fields are present but empty", () => {
    const result = validateMetadataJson('{"make":"","model":"CBR","year":2023,"category":"sport"}');
    expect(result.ok).toBe(false);
    expect(result.error).toMatch(/make/i);
  });

  it("rejects a non-numeric year", () => {
    const result = validateMetadataJson('{"make":"Honda","model":"CBR","year":"2023","category":"sport"}');
    expect(result.ok).toBe(false);
    expect(result.error).toMatch(/year.*number/i);
  });
});
