import { invoke } from "@tauri-apps/api/core";

export interface QueueLocalIngestionWorkItemRequest {
  sourcePath: string;
  jobId: string;
  uploadId: string;
  processorRunId: string;
  documentType: string;
  createdAtUtc: string;
  sourceFileName?: string;
  size?: number;
}

export interface QueueLocalIngestionWorkItemResult {
  watchFolder: string;
  manifestFileName: string;
  pairedLocalFileName: string;
}

export type LocalFileWithPath = File & {
  path?: string;
};

export function pickLocalIngestionFile(): Promise<string | null> {
  return invoke<string | null>("pick_local_ingestion_file");
}

export function getTauriFilePath(file: File): string {
  const path = (file as LocalFileWithPath).path;
  if (typeof path === "string" && path.trim().length > 0) {
    return path;
  }

  throw new Error(
    "The selected file did not include a local path. Re-select it from the desktop app so it can be queued for local processing.",
  );
}

export function queueLocalIngestionWorkItem(
  request: QueueLocalIngestionWorkItemRequest,
): Promise<QueueLocalIngestionWorkItemResult> {
  return invoke<QueueLocalIngestionWorkItemResult>("queue_local_ingestion_work_item", {
    request,
  });
}
