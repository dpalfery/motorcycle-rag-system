import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ProcessorScreen from "./ProcessorScreen";
import { useConfig, DEFAULT_CONFIG } from "@/lib/config";
import type { IngestionJobStatus } from "@/lib/ingestionJob";

// --- Mock the cloud API client at the axios boundary ---
const { apiGet, apiPost, apiDelete } = vi.hoisted(() => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiDelete: vi.fn(),
}));

vi.mock("@/lib/apiClient", () => ({
  api: { get: apiGet, post: apiPost, delete: apiDelete },
}));

// --- Mock the local processor Tauri bridge (not running in tests) ---
vi.mock("@/lib/processor", () => ({
  isMissingProcessorJobError: () => false,
  toStartConfig: vi.fn(() => ({
    port: 8100,
    workingDir: "",
    embeddingProviderEndpoint: "",
    embeddingModel: "",
    tokenizerModelPath: "",
    graphExtractionEndpoint: "",
    graphExtractionModel: "",
    apiBaseUrl: "",
    azureStorageAccountUrl: "",
  })),
  processor: {
    isListening: vi.fn().mockResolvedValue(false),
    health: vi.fn().mockResolvedValue({ status: "healthy", accepting_work: true }),
    jobs: vi.fn().mockResolvedValue([]),
    start: vi.fn(),
    stop: vi.fn(),
    cleanupJobs: vi.fn(),
    stopJob: vi.fn(),
  },
}));

// --- Mock local ingestion helpers (Tauri file pickers; not exercised here) ---
vi.mock("@/lib/localIngestion", () => ({
  getTauriFilePath: vi.fn(),
  pickLocalIngestionFile: vi.fn(),
  queueLocalIngestionWorkItem: vi.fn(),
}));

function makeJob(overrides: Partial<IngestionJobStatus> = {}): IngestionJobStatus {
  return {
    jobId: "job-1",
    id: 1,
    status: "Completed",
    createdAtUtc: new Date().toISOString(),
    inputType: "manual-pdf",
    inputRef: "blob://upload.pdf",
    ...overrides,
  };
}

/**
 * Programs the cloud API to return the given cloud jobs plus valid upload constraints.
 * The local processor is reported as not-listening, so only the cloud jobs table renders.
 */
function configureCloudJobs(jobs: IngestionJobStatus[]) {
  apiGet.mockImplementation(async (url: string) => {
    if (url.includes("upload-constraints")) {
      return { data: { maxFileSizeBytes: 104_857_600, supportedExtensions: [".pdf", ".csv"] } };
    }
    if (url === "/api/ingestion/jobs") {
      return { data: jobs };
    }
    // Per-job status fetch fallback.
    return { data: jobs[0] ?? {} };
  });
  apiPost.mockResolvedValue({ data: {} });
  apiDelete.mockResolvedValue({ data: {} });
}

function renderScreen() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0, staleTime: Infinity, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  });
  useConfig.setState({ config: { ...DEFAULT_CONFIG }, loaded: true });

  return render(
    <QueryClientProvider client={queryClient}>
      <ProcessorScreen />
    </QueryClientProvider>,
  );
}

describe("ProcessorScreen — cloud job source file name subtitle", () => {
  beforeEach(() => {
    apiGet.mockReset();
    apiPost.mockReset();
    apiDelete.mockReset();
  });
  afterEach(() => cleanup());

  it("renders sourceFileName as a subtitle under the job label when present", async () => {
    configureCloudJobs([
      makeJob({ id: 50, jobId: "job-50", sourceFileName: "Yamaha-MT07-2022.pdf" }),
    ]);
    renderScreen();

    await screen.findByText("Job 50");

    expect(screen.getByText("Yamaha-MT07-2022.pdf")).toBeInTheDocument();
  });

  it("does not render a subtitle when sourceFileName is undefined", async () => {
    configureCloudJobs([makeJob({ id: 51, jobId: "job-51", sourceFileName: undefined })]);
    renderScreen();

    await screen.findByText("Job 51");

    expect(screen.queryByText(/\.pdf$/i)).not.toBeInTheDocument();
  });
});
