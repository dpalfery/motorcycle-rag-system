import { api } from "./apiClient";

/**
 * API helpers for the manual metadata fallback flow (PDF metadata extraction Phase 4).
 *
 * The cloud .NET API exposes:
 *   POST /api/ingestion/jobs/{jobId}/metadata  — admin submits manual metadata JSON.
 *   GET  /api/ingestion/jobs/{jobId}/metadata  — fetch the current metadata (for pre-fill).
 *
 * Both endpoints require the `mcr-api-admin` role on the server side. The shared `api`
 * axios instance attaches the Entra bearer token via its request interceptor, so callers
 * do not need to handle auth here.
 */

/** POST body for manual metadata submission (mirrors C# `ManualMetadataSubmitRequest`). */
export interface ManualMetadataSubmitRequest {
  metadataJson: string;
}

/** GET response shape (mirrors C# `IngestionJobMetadataResponse`). */
export interface IngestionJobMetadataResponse {
  jobId: string;
  make?: string | null;
  model?: string | null;
  year?: number | null;
  category?: string | null;
  tags?: string[];
  fillRate?: number;
  isComplete?: boolean;
  rawJson?: string | null;
}

/** Result of client-side metadata JSON validation. */
export interface MetadataValidationResult {
  ok: boolean;
  value?: Record<string, unknown>;
  error?: string;
}

const REQUIRED_FIELDS = ["make", "model", "year", "category"] as const;

/**
 * Submit manually entered metadata JSON for a paused job. Throws the underlying axios error
 * on non-2xx responses; callers should format it via `formatAdminError`.
 */
export async function submitManualMetadata(
  jobId: string,
  metadataJson: string,
): Promise<void> {
  const body: ManualMetadataSubmitRequest = { metadataJson };
  await api.post(`/api/ingestion/jobs/${jobId}/metadata`, body);
}

/**
 * Fetch the current metadata for an ingestion job (used to pre-fill the manual entry modal).
 * Throws the underlying axios error on non-2xx responses.
 */
export async function getJobMetadata(
  jobId: string,
): Promise<IngestionJobMetadataResponse> {
  const res = await api.get<IngestionJobMetadataResponse>(
    `/api/ingestion/jobs/${jobId}/metadata`,
  );
  return res.data;
}

/**
 * Pure client-side validation for the manual metadata JSON textarea. Confirms the input is
 * parseable JSON, is a single object, contains all required fields, and that `year` is a
 * finite number. OWASP ASVS L2 input validation is also enforced server-side; this is a
 * fast feedback layer only.
 */
export function validateMetadataJson(raw: string): MetadataValidationResult {
  const trimmed = raw.trim();
  if (!trimmed) {
    return { ok: false, error: "Metadata JSON is required." };
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch {
    return { ok: false, error: "Please enter valid JSON." };
  }

  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    return { ok: false, error: "Metadata must be a single JSON object." };
  }

  const obj = parsed as Record<string, unknown>;
  const missing = REQUIRED_FIELDS.filter((field) => {
    const value = obj[field];
    return value === undefined || value === null || value === "";
  });

  if (missing.length > 0) {
    return { ok: false, error: `Missing required field(s): ${missing.join(", ")}.` };
  }

  if (typeof obj.year !== "number" || !Number.isFinite(obj.year)) {
    return { ok: false, error: 'Field "year" must be a number.' };
  }

  return { ok: true, value: obj };
}

/**
 * Render an `IngestionJobMetadataResponse` as a pretty-printed JSON string suitable for
 * pre-filling the modal textarea. Prefers the server-provided `rawJson` when present.
 */
export function metadataResponseToJson(meta: IngestionJobMetadataResponse): string {
  if (meta.rawJson && meta.rawJson.trim()) {
    return meta.rawJson;
  }

  const obj: Record<string, unknown> = {};
  if (meta.make) obj.make = meta.make;
  if (meta.model) obj.model = meta.model;
  // `year: 0` is the extractor's "could not determine" sentinel, so exclude it from pre-fill.
  if (typeof meta.year === "number" && meta.year > 0) obj.year = meta.year;
  if (meta.category) obj.category = meta.category;
  if (meta.tags && meta.tags.length > 0) obj.tags = meta.tags;

  return JSON.stringify(obj, null, 2);
}
