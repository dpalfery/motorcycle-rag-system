import { describe, expect, it, vi } from "vitest";
import { invoke } from "@tauri-apps/api/core";
import {
  getTauriFilePath,
  pickLocalIngestionFile,
  queueLocalIngestionWorkItem,
  type LocalFileWithPath,
} from "./localIngestion";

vi.mock("@tauri-apps/api/core", () => ({
  invoke: vi.fn(),
}));

describe("getTauriFilePath", () => {
  it("returns the Tauri-provided local file path", () => {
    const file = new File(["contents"], "manual.pdf") as LocalFileWithPath;
    file.path = "/Users/dave/manual.pdf";

    expect(getTauriFilePath(file)).toBe("/Users/dave/manual.pdf");
  });

  it("fails when the selected file has no local path", () => {
    const file = new File(["contents"], "manual.pdf");

    expect(() => getTauriFilePath(file)).toThrow("local path");
  });
});

describe("queueLocalIngestionWorkItem", () => {
  it("invokes the Rust local ingestion queue command", async () => {
    vi.mocked(invoke).mockResolvedValue({
      watchFolder: "/app-data/local-ingestion-watch",
      manifestFileName: "processor-run.json",
      pairedLocalFileName: "upload-manual.pdf",
    });

    const request = {
      sourcePath: "/Users/dave/manual.pdf",
      jobId: "job-1",
      uploadId: "upload-1",
      processorRunId: "processor-run-1",
      documentType: "manual-pdf",
      sourceFileName: "manual.pdf",
      size: 123,
      createdAtUtc: "2026-07-04T12:00:00.000Z",
    };

    await queueLocalIngestionWorkItem(request);

    expect(invoke).toHaveBeenCalledWith("queue_local_ingestion_work_item", {
      request,
    });
  });
});

describe("pickLocalIngestionFile", () => {
  it("invokes the Rust file picker command", async () => {
    vi.mocked(invoke).mockResolvedValue("/Users/dave/manual.pdf");

    await expect(pickLocalIngestionFile()).resolves.toBe("/Users/dave/manual.pdf");

    expect(invoke).toHaveBeenCalledWith("pick_local_ingestion_file");
  });
});
