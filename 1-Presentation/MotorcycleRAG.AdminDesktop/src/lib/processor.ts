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
  uploadJobSecret?: string;
  apiBaseUrl: string;
}

export interface HealthResponse {
  status: string;
  accepting_work?: boolean;
  shutdown_requested?: boolean;
  active_jobs?: number;
  message?: string;
  services?: Record<string, unknown>;
}

export interface ProcessorJob {
  job_id: string;
  status: string;
  message?: string;
  progress?: number;
  document_type?: string;
  created_at?: string;
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

export function toStartConfig(c: AppConfig, uploadJobSecret?: string): ProcessorStartConfig {
  return {
    port: c.localProcessorPort,
    workingDir: c.localProcessorWorkingDir,
    embeddingProviderEndpoint: c.embeddingProviderEndpoint,
    embeddingModel: c.embeddingModel,
    apiBaseUrl: c.apiBaseUrl,
    uploadJobSecret,
  };
}

export const processor = {
  start: (config: ProcessorStartConfig) => invoke<void>("processor_start", { config }),
  stop: () => invoke<void>("processor_stop"),
  isRunning: () => invoke<boolean>("processor_running"),

  /** Proxy an HTTP request to the local processor; returns parsed JSON. */
  request: <T = unknown>(method: string, path: string, body?: unknown, port?: number) =>
    invoke<T>("processor_request", { method, path, body: body ?? null, port: port ?? null }),

  health: (port?: number) => processor.request<HealthResponse>("GET", "/health", undefined, port),
  jobs: (port?: number) => processor.request<ProcessorJob[]>("GET", "/jobs", undefined, port),
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
