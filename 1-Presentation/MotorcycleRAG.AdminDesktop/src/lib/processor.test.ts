import { beforeEach, describe, it, expect, vi } from "vitest";
import {
  discoverProcessorWorkingDir,
  ensureProcessorReady,
  isMissingProcessorJobError,
  processor,
} from "./processor";
import { invoke } from "@tauri-apps/api/core";
import type { AppConfig } from "./config";

vi.mock("@tauri-apps/api/core", () => ({
  invoke: vi.fn(),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

const baseConfig: AppConfig = {
        apiBaseUrl: "https://motorag.api.palfery.com",
  authAuthority: "https://login.microsoftonline.com/tenant",
  authClientId: "client-id",
  authScope: "api://scope",
  embeddingProviderEndpoint: "http://localhost:1234/v1",
  embeddingModel: "text-embedding-qwen",
  tokenizerModelPath: "/models/qwen-tokenizer",
  graphExtractionEndpoint: "http://localhost:1234/v1",
  graphExtractionModel: "microsoft/phi-4-reasoning-plus",
  localProcessorPort: 8100,
  localProcessorWorkingDir: "/repo/2-Application/local-processing-service",
  azureStorageAccountUrl: "https://storage.example/",
  autoResolveProcessor: true,
  pythonUploadJobSecret: "",
  selectedChromeProfile: "Default",
};

describe("processor discovery", () => {
  it("calls the backend deterministic resolver", async () => {
    const expectedPath = "/repo/2-Application/local-processing-service";
    vi.mocked(invoke).mockResolvedValue(expectedPath);

    const result = await discoverProcessorWorkingDir();
    
    expect(invoke).toHaveBeenCalledWith("resolve_processor_path", { configuredOverride: "" });
    expect(result).toBe(expectedPath);
  });

  it("handles backend failures gracefully", async () => {
    vi.mocked(invoke).mockRejectedValue(new Error("Resolution failed"));

    const result = await discoverProcessorWorkingDir();
    expect(result).toBe("");
  });
});

describe("processor job control", () => {
  it("posts a per-job stop request through the local processor proxy", async () => {
    vi.mocked(invoke).mockResolvedValue({
      job_id: "run-22",
      status: "cancelled",
    });

    const result = await processor.stopJob("run-22", 8100);

    expect(invoke).toHaveBeenCalledWith("processor_request", {
      method: "POST",
      path: "/jobs/run-22/stop",
      body: null,
      port: 8100,
    });
    expect(result.status).toBe("cancelled");
  });

  it("recognizes the local processor's stale job 404 response", () => {
    expect(
      isMissingProcessorJobError('404 Not Found: {"detail":"Job not found"}')
    ).toBe(true);
    expect(
      isMissingProcessorJobError(new Error('404 Not Found: {"detail":"Job not found"}'))
    ).toBe(true);
    expect(isMissingProcessorJobError("500 Internal Server Error")).toBe(false);
  });
});

describe("ensureProcessorReady", () => {
  it("keeps a running processor when health config matches settings", async () => {
    vi.mocked(invoke).mockImplementation(async (command) => {
      if (command === "processor_is_listening") return true;
      if (command === "processor_request") {
        return {
          status: "healthy",
          accepting_work: true,
          shutdown_requested: false,
          services: {
            blob_storage: true,
            embedding_endpoint: "http://localhost:1234/v1/",
            embedding_model: "text-embedding-qwen",
            tokenizer_path: "/models/qwen-tokenizer",
          },
        };
      }
      throw new Error(`unexpected command ${command}`);
    });

    const result = await ensureProcessorReady(baseConfig);

    expect(result).toBe(baseConfig);
    expect(invoke).not.toHaveBeenCalledWith("processor_stop", expect.anything());
    expect(invoke).not.toHaveBeenCalledWith("processor_start", expect.anything());
  });

  it("restarts a running processor when health config does not match settings", async () => {
    let healthCalls = 0;
    vi.mocked(invoke).mockImplementation(async (command) => {
      if (command === "processor_is_listening") return true;
      if (command === "processor_stop") return undefined;
      if (command === "processor_start") return undefined;
      if (command === "processor_request") {
        healthCalls += 1;
        return {
          status: "healthy",
          accepting_work: true,
          shutdown_requested: false,
          services: {
            blob_storage: true,
            embedding_endpoint:
              healthCalls === 1 ? "http://localhost:11434" : "http://localhost:1234/v1",
            embedding_model: healthCalls === 1 ? "qwen3-embedding" : "text-embedding-qwen",
            tokenizer_path: "/models/qwen-tokenizer",
          },
        };
      }
      throw new Error(`unexpected command ${command}`);
    });

    await ensureProcessorReady(baseConfig, { timeoutMs: 1_000 });

    expect(invoke).toHaveBeenCalledWith("processor_stop", { port: 8100 });
    expect(invoke).toHaveBeenCalledWith("processor_start", {
      config: {
        port: 8100,
        workingDir: "/repo/2-Application/local-processing-service",
        embeddingProviderEndpoint: "http://localhost:1234/v1",
        embeddingModel: "text-embedding-qwen",
        tokenizerModelPath: "/models/qwen-tokenizer",
        graphExtractionEndpoint: "http://localhost:1234/v1",
        graphExtractionModel: "microsoft/phi-4-reasoning-plus",
  apiBaseUrl: "https://motorag.api.palfery.com",
        azureStorageAccountUrl: "https://storage.example/",
        uploadJobSecret: undefined,
      },
    });
  });

  it("restarts a matching processor when it is not accepting work", async () => {
    let healthCalls = 0;
    vi.mocked(invoke).mockImplementation(async (command) => {
      if (command === "processor_is_listening") return true;
      if (command === "processor_stop") return undefined;
      if (command === "processor_start") return undefined;
      if (command === "processor_request") {
        healthCalls += 1;
        return {
          status: "healthy",
          accepting_work: healthCalls !== 1,
          shutdown_requested: healthCalls === 1,
          services: {
            blob_storage: true,
            embedding_endpoint: "http://localhost:1234/v1",
            embedding_model: "text-embedding-qwen",
            tokenizer_path: "/models/qwen-tokenizer",
          },
        };
      }
      throw new Error(`unexpected command ${command}`);
    });

    await ensureProcessorReady(baseConfig, { timeoutMs: 1_000 });

    expect(invoke).toHaveBeenCalledWith("processor_stop", { port: 8100 });
    expect(invoke).toHaveBeenCalledWith("processor_start", expect.anything());
  });

  it("restarts a matching processor when tokenizer path does not match settings", async () => {
    let healthCalls = 0;
    vi.mocked(invoke).mockImplementation(async (command) => {
      if (command === "processor_is_listening") return true;
      if (command === "processor_stop") return undefined;
      if (command === "processor_start") return undefined;
      if (command === "processor_request") {
        healthCalls += 1;
        return {
          status: "healthy",
          accepting_work: true,
          shutdown_requested: false,
          services: {
            blob_storage: true,
            embedding_endpoint: "http://localhost:1234/v1",
            embedding_model: "text-embedding-qwen",
            tokenizer_path:
              healthCalls === 1 ? "/models/old-tokenizer" : "/models/qwen-tokenizer",
          },
        };
      }
      throw new Error(`unexpected command ${command}`);
    });

    await ensureProcessorReady(baseConfig, { timeoutMs: 1_000 });

    expect(invoke).toHaveBeenCalledWith("processor_stop", { port: 8100 });
    expect(invoke).toHaveBeenCalledWith("processor_start", expect.anything());
  });
});
