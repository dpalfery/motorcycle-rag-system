import { invoke } from "@tauri-apps/api/core";
import type { AppConfig } from "./config";

/**
 * Bridge to the Rust commands that supervise and proxy the local Python processor
 * (2-Application/local-processing-service). Calls go through Rust (not browser fetch)
 * to avoid CORS / mixed-content against http://127.0.0.1. Mirrors the routes the MAUI
 * LocalProcessorService used.
 */

export interface ProcessorStartConfig {
  port: number;
  workingDir: string;
  embeddingProviderEndpoint: string;
  embeddingModel: string;
  tokenizerModelPath: string;
  graphExtractionEndpoint: string;
  graphExtractionModel: string;
  uploadJobSecret?: string;
  apiBaseUrl: string;
  azureStorageAccountUrl: string;
}

export interface HealthResponse {
  status: string;
  accepting_work?: boolean;
  shutdown_requested?: boolean;
  active_jobs?: number;
  message?: string;
  api_client_configured?: boolean;
  services?: {
    embedding_provider?: unknown;
    embedding_endpoint?: unknown;
    embedding_model?: unknown;
    blob_storage?: unknown;
    service_uptime?: unknown;
    [key: string]: unknown;
  };
}

export interface ProcessorJob {
  job_id: string;
  status: string;
  message?: string;
  progress?: number;
  stage?: string;
  stage_index?: number;
  total_stages?: number;
  chunks_processed?: number;
  total_chunks?: number;
  document_type?: string;
  created_at?: string;
}

const DEFAULT_READY_TIMEOUT_MS = 45_000;
const READY_POLL_INTERVAL_MS = 500;

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Attempts to resolve the local processor path using the backend's deterministic resolver.
 */
export async function discoverProcessorWorkingDir(): Promise<string> {
  try {
    return await invoke<string>("resolve_processor_path", { configuredOverride: "" });
  } catch (e) {
    console.warn("Path auto-resolution failed:", e);
    return "";
  }
}

export function toStartConfig(c: AppConfig): ProcessorStartConfig {
  return {
    port: c.localProcessorPort,
    workingDir: c.localProcessorWorkingDir,
    embeddingProviderEndpoint: c.embeddingProviderEndpoint,
    embeddingModel: c.embeddingModel,
    tokenizerModelPath: c.tokenizerModelPath,
    graphExtractionEndpoint: c.graphExtractionEndpoint,
    graphExtractionModel: c.graphExtractionModel,
    apiBaseUrl: c.apiBaseUrl,
    azureStorageAccountUrl: c.azureStorageAccountUrl,
    uploadJobSecret: c.pythonUploadJobSecret?.trim() || undefined,
  };
}

function blobStorageReady(health: HealthResponse): boolean {
  return health.services?.blob_storage === true;
}

function normalizeEndpoint(value: string): string {
  return value.trim().replace(/\/+$/, "").toLowerCase();
}

function processorConfigMatches(health: HealthResponse, config: AppConfig): boolean {
  const activeEndpoint = health.services?.embedding_endpoint;
  const activeModel = health.services?.embedding_model;
  const activeTokenizerPath = health.services?.tokenizer_path;

  if (typeof activeEndpoint !== "string" || typeof activeModel !== "string") {
    return false;
  }

  const expectedTokenizerPath = config.tokenizerModelPath.trim();
  const tokenizerMatches =
    !expectedTokenizerPath ||
    (typeof activeTokenizerPath === "string" &&
      activeTokenizerPath.trim() === expectedTokenizerPath);

  const apiConfigured = config.pythonUploadJobSecret.trim().length > 0;
  const healthApiConfigured = health.api_client_configured === true;
  const apiConfigMatches = !apiConfigured || healthApiConfigured;

  return (
    normalizeEndpoint(activeEndpoint) === normalizeEndpoint(config.embeddingProviderEndpoint) &&
    activeModel.trim() === config.embeddingModel.trim() &&
    tokenizerMatches &&
    apiConfigMatches
  );
}

function processorAcceptsWork(health: HealthResponse): boolean {
  return (
    health.status.toLowerCase() === "healthy" &&
    health.accepting_work === true &&
    health.shutdown_requested !== true
  );
}

async function processorIsReady(port: number, config: AppConfig): Promise<boolean> {
  if (!(await processor.isListening(port))) {
    return false;
  }

  try {
    const health = await processor.health(port);
    return (
      processorAcceptsWork(health) &&
      blobStorageReady(health) &&
      processorConfigMatches(health, config)
    );
  } catch {
    return false;
  }
}

async function resolveWorkingDir(config: AppConfig): Promise<string> {
  if (config.localProcessorWorkingDir.trim()) {
    return config.localProcessorWorkingDir;
  }

  const resolved = await discoverProcessorWorkingDir();
  if (!resolved) {
    throw new Error(
      "Local processor path is not configured and could not be auto-resolved. Set it in Settings."
    );
  }

  return resolved;
}

/**
 * Ensures the local Python processor is listening before the cloud API triggers a pipeline run.
 */
export async function ensureProcessorReady(
  config: AppConfig,
  options?: { timeoutMs?: number }
): Promise<AppConfig> {
  const timeoutMs = options?.timeoutMs ?? DEFAULT_READY_TIMEOUT_MS;
  const workingDir = await resolveWorkingDir(config);
  const readyConfig =
    workingDir === config.localProcessorWorkingDir
      ? config
      : { ...config, localProcessorWorkingDir: workingDir };

  const port = readyConfig.localProcessorPort;
  if (await processorIsReady(port, readyConfig)) {
    return readyConfig;
  }

  if (await processor.isListening(port)) {
    await processor.stop(port);
  }

  await processor.start(toStartConfig(readyConfig));

  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await processorIsReady(port, readyConfig)) {
      return readyConfig;
    }
    await sleep(READY_POLL_INTERVAL_MS);
  }

  throw new Error(
    `Local processor did not become ready on 127.0.0.1:${port} within ${Math.round(timeoutMs / 1000)}s.`
  );
}

export const processor = {
  start: (config: ProcessorStartConfig) => invoke<void>("processor_start", { config }),
  stop: (port?: number) => invoke<void>("processor_stop", { port: port ?? null }),
  isRunning: () => invoke<boolean>("processor_running"),
  isListening: (port?: number) =>
    invoke<boolean>("processor_is_listening", { port: port ?? null }),

  /** Proxy an HTTP request to the local processor; returns parsed JSON. */
  request: <T = unknown>(method: string, path: string, body?: unknown, port?: number) =>
    invoke<T>("processor_request", { method, path, body: body ?? null, port: port ?? null }),

  health: (port?: number) => processor.request<HealthResponse>("GET", "/health", undefined, port),
  jobs: (port?: number) => processor.request<ProcessorJob[]>("GET", "/jobs", undefined, port),
  stopJob: (jobId: string, port?: number) =>
    processor.request<ProcessorJob>("POST", `/jobs/${encodeURIComponent(jobId)}/stop`, undefined, port),
  cleanupJobs: (port?: number) => processor.request("POST", "/jobs/cleanup", undefined, port),
  shutdown: (port?: number) => processor.request("POST", "/control/shutdown", undefined, port),
  discoverModels: (endpoint: string, port?: number) =>
    processor.request<{ provider: string; endpoint: string; models: string[] }>(
      "GET",
      `/embedding/models?endpoint=${encodeURIComponent(endpoint)}`,
      undefined,
      port,
    ),
};
