import axios from "axios";

export function sanitizeForLog(error: unknown): unknown {
  if (error == null || typeof error !== 'object') return error;
  const e = error as Record<string, unknown>;
  if (!e.config) return error;
  const config = { ...(e.config as Record<string, unknown>) };
  if (config.headers && typeof config.headers === 'object') {
    const headers = { ...(config.headers as Record<string, unknown>) };
    Object.keys(headers).forEach((k) => {
      if (['authorization', 'x-api-key', 'x-auth-token', 'cookie'].includes(k.toLowerCase())) {
        headers[k] = '[REDACTED]';
      }
    });
    config.headers = headers;
  }
  return { ...e, config };
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  traceId?: string;
  referenceId?: string;
  extensions?: {
    traceId?: string;
    referenceId?: string;
  };
}

type AdminErrorContext =
  | {
      action: string;
      kind: "cloud-api";
      apiBaseUrl?: string;
    }
  | {
      action: string;
      kind: "local-processor";
      localProcessorPort?: number;
      location?: "integration" | "endpoint";
    }
  | {
      action: string;
      kind: "local-queue";
    };

function trimTrailingSlash(value: string | undefined) {
  return value?.replace(/\/+$/, "");
}

function joinUrl(baseUrl: string | undefined, path: string | undefined) {
  if (!path) {
    return trimTrailingSlash(baseUrl) ?? "configured API base URL";
  }

  if (/^https?:\/\//i.test(path)) {
    return path;
  }

  const normalizedBase = trimTrailingSlash(baseUrl);
  if (!normalizedBase) {
    return path;
  }

  return `${normalizedBase}${path.startsWith("/") ? path : `/${path}`}`;
}

function formatTrace(problem: ProblemDetails | undefined) {
  const traceId = problem?.traceId ?? problem?.extensions?.traceId;
  const referenceId = problem?.referenceId ?? problem?.extensions?.referenceId;
  const parts = [
    traceId ? `trace: ${traceId}` : null,
    referenceId ? `reference: ${referenceId}` : null,
  ].filter(Boolean);

  return parts.length > 0 ? ` (${parts.join(", ")})` : "";
}

function formatCloudApiError(
  error: unknown,
  context: Extract<AdminErrorContext, { kind: "cloud-api" }>,
) {
  if (!axios.isAxiosError<ProblemDetails>(error)) {
    return error instanceof Error ? error.message : String(error);
  }

  const method = error.config?.method?.toUpperCase() ?? "REQUEST";
  const requestUrl = joinUrl(context.apiBaseUrl ?? error.config?.baseURL, error.config?.url);

  if (error.response) {
    const problem = error.response.data;
    const message = problem?.detail ?? problem?.title ?? error.message;

    return `${context.action} failed at the cloud API (${method} ${requestUrl}, HTTP ${error.response.status}). ${message}${formatTrace(problem)}`;
  }

  const transportMessage = error.message?.trim() ? ` Transport error: ${error.message}.` : "";
  return `${context.action} failed before the cloud API returned a response (${method} ${requestUrl}).${transportMessage} This points to the Admin Desktop -> cloud API connection, such as the API being offline, the API base URL being wrong, or a CORS/TLS/reachability problem. It does not identify the local processor or LM Studio as the failing dependency.`;
}

function formatLocalProcessorError(
  error: unknown,
  context: Extract<AdminErrorContext, { kind: "local-processor" }>,
) {
  const detail = error instanceof Error ? error.message : String(error);
  if (context.location !== "endpoint") {
    return `${context.action} failed in the desktop app's local processor integration. ${detail}`;
  }

  const endpoint = context.localProcessorPort
    ? `127.0.0.1:${context.localProcessorPort}`
    : "the configured local processor endpoint";

  return `${context.action} failed at the local processor endpoint (${endpoint}). ${detail}`;
}

function formatLocalQueueError(
  error: unknown,
  context: Extract<AdminErrorContext, { kind: "local-queue" }>,
) {
  const detail = error instanceof Error ? error.message : String(error);
  return `${context.action} failed while the desktop app was queueing the file into the local watch folder. ${detail}`;
}

export function formatAdminError(error: unknown, context: AdminErrorContext): string {
  if (context.kind === "cloud-api") {
    return formatCloudApiError(error, context);
  }

  if (context.kind === "local-processor") {
    return formatLocalProcessorError(error, context);
  }

  return formatLocalQueueError(error, context);
}
