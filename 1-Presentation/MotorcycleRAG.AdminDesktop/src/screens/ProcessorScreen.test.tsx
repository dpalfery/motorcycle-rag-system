import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
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

const {
  isListening,
  health,
  processorJobs,
  processorStart,
  processorStop,
  cleanupJobs,
  stopJob,
  isMissingProcessorJobError,
  pickLocalIngestionFile,
  getTauriFilePath,
  queueLocalIngestionWorkItem,
} = vi.hoisted(() => ({
  isListening: vi.fn(),
  health: vi.fn(),
  processorJobs: vi.fn(),
  processorStart: vi.fn(),
  processorStop: vi.fn(),
  cleanupJobs: vi.fn(),
  stopJob: vi.fn(),
  isMissingProcessorJobError: vi.fn(),
  pickLocalIngestionFile: vi.fn(),
  getTauriFilePath: vi.fn(),
  queueLocalIngestionWorkItem: vi.fn(),
}));

vi.mock("@/lib/apiClient", () => ({
  api: { get: apiGet, post: apiPost, delete: apiDelete },
}));

// --- Mock the local processor Tauri bridge (not running in tests) ---
vi.mock("@/lib/processor", () => ({
  isMissingProcessorJobError,
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
    isListening,
    health,
    jobs: processorJobs,
    start: processorStart,
    stop: processorStop,
    cleanupJobs,
    stopJob,
  },
}));

// --- Mock local ingestion helpers (Tauri file pickers; not exercised here) ---
vi.mock("@/lib/localIngestion", () => ({
  getTauriFilePath,
  pickLocalIngestionFile,
  queueLocalIngestionWorkItem,
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

function renderScreen(configOverrides: Partial<typeof DEFAULT_CONFIG> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0, staleTime: Infinity, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  });
  useConfig.setState({
    config: { ...DEFAULT_CONFIG, ...configOverrides },
    loaded: true,
  });

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
    isListening.mockReset().mockResolvedValue(false);
    health.mockReset().mockResolvedValue({
      status: "healthy",
      accepting_work: true,
      api_client_configured: true,
    });
    processorJobs.mockReset().mockResolvedValue([]);
    processorStart.mockReset().mockResolvedValue(undefined);
    processorStop.mockReset().mockResolvedValue(undefined);
    cleanupJobs.mockReset().mockResolvedValue(undefined);
    stopJob.mockReset().mockResolvedValue(undefined);
    isMissingProcessorJobError.mockReset().mockReturnValue(false);
    pickLocalIngestionFile.mockReset();
    getTauriFilePath.mockReset();
    queueLocalIngestionWorkItem.mockReset().mockResolvedValue(undefined);
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

  it("starts a stopped processor and refreshes processor state", async () => {
    configureCloudJobs([]);
    renderScreen();
    expect(await screen.findByText("Stopped")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /start/i }));

    await waitFor(() => expect(processorStart).toHaveBeenCalledOnce());
    expect(screen.getByText("Start the processor to see jobs.")).toBeInTheDocument();
  });

  it("shows and dismisses processor start errors", async () => {
    configureCloudJobs([]);
    processorStart.mockRejectedValue(new Error("binary missing"));
    const { container } = renderScreen();
    await screen.findByText("Stopped");

    fireEvent.click(screen.getByRole("button", { name: /start/i }));
    expect(await screen.findByText(/Could not start the processor:/)).toBeInTheDocument();
    const errorBox = screen.getByText(/Could not start the processor:/).closest("div")!;
    fireEvent.click(errorBox.querySelector("button")!);
    await waitFor(() =>
      expect(screen.queryByText(/Could not start the processor:/)).not.toBeInTheDocument(),
    );
    expect(container).toBeInTheDocument();
  });

  it("renders running health, service status, and local job progress", async () => {
    configureCloudJobs([]);
    isListening.mockResolvedValue(true);
    health.mockResolvedValue({
      status: "healthy",
      accepting_work: true,
      api_client_configured: true,
      active_jobs: 2,
      services: {
        embedding_endpoint: "http://localhost:1234/v1",
        embedding_model: "embed-model",
        graph_extraction: {
          endpoint: "http://localhost:1234/v1",
          model: "graph-model",
          status: "degraded",
        },
      },
    });
    processorJobs.mockResolvedValue([
      {
        job_id: "pdf-job-123456",
        document_type: "manual-pdf",
        status: "running",
        stage: "extract-graph",
        progress: 0.4,
        chunks_processed: 4,
        total_chunks: 10,
      },
      {
        job_id: "csv-job-123456",
        document_type: "spec-csv",
        status: "completed",
        progress: 1,
      },
    ]);
    renderScreen();

    expect(await screen.findByText("Running")).toBeInTheDocument();
    expect(await screen.findByText("Service status")).toBeInTheDocument();
    expect(screen.getByText("embed-model")).toBeInTheDocument();
    expect(screen.getByText("graph-model")).toBeInTheDocument();
    expect(screen.getByText(/4\/10 chunks/)).toBeInTheDocument();
    expect(screen.getByText("Extract Graph")).toBeInTheDocument();
    expect(screen.getAllByText("Done").length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole("button", { name: /clean up finished/i }));
    await waitFor(() => expect(cleanupJobs).toHaveBeenCalledWith(8100));
    fireEvent.click(screen.getByRole("button", { name: /^stop$/i }));
    await waitFor(() => expect(processorStop).toHaveBeenCalledWith(8100));
  });

  it("shows local job loading and errors while running", async () => {
    configureCloudJobs([]);
    isListening.mockResolvedValue(true);
    health.mockResolvedValue({
      status: "ok",
      accepting_work: true,
      api_client_configured: false,
      services: {},
    });
    processorJobs.mockRejectedValue(new Error("processor endpoint unavailable"));
    renderScreen();

    expect(await screen.findByText(/Could not load local jobs:/)).toBeInTheDocument();
    expect(screen.getByText("Not configured")).toBeInTheDocument();
    expect(screen.getByText("Missing")).toBeInTheDocument();
  });

  it("selects a local file and clears the selection", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockResolvedValue("/manuals/Honda.pdf");
    const { container } = renderScreen();
    await screen.findByText("Stopped");

    fireEvent.click(screen.getByText(/Click to browse or drag a file here/));
    expect(await screen.findByText("Honda.pdf")).toBeInTheDocument();
    expect(screen.getByText("/manuals/Honda.pdf")).toBeInTheDocument();

    const selected = screen.getByText("Honda.pdf").closest("div")!.parentElement!.parentElement!;
    fireEvent.click(selected.querySelector("button")!);
    expect(screen.getByText(/Click to browse or drag a file here/)).toBeInTheDocument();
    expect(container).toBeInTheDocument();
  });

  it("reports picker errors and rejected dropped files", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockRejectedValue(new Error("picker unavailable"));
    const { container } = renderScreen();
    await screen.findByText("Stopped");

    const dropZone = screen.getByText(/Click to browse or drag a file here/).parentElement!;
    fireEvent.click(dropZone);
    expect(await screen.findByText("picker unavailable")).toBeInTheDocument();

    const huge = new File(["x"], "manual.pdf");
    Object.defineProperty(huge, "size", { value: 200 * 1024 * 1024 });
    fireEvent.drop(dropZone, { dataTransfer: { files: [huge] } });
    expect(screen.getByText(/File exceeds the 100 MB limit/)).toBeInTheDocument();

    fireEvent.drop(dropZone, { dataTransfer: { files: [new File(["x"], "notes.txt")] } });
    expect(screen.getByText(/File type not supported/)).toBeInTheDocument();
    expect(container).toBeInTheDocument();
  });

  it("queues a selected PDF through cloud and local ingestion", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockResolvedValue("/manuals/Honda.pdf");
    apiPost.mockResolvedValue({
      data: makeJob({ status: "Queued", docIngestionRunId: "run-1" }),
    });
    renderScreen({ pythonUploadJobSecret: "secret" });
    await screen.findByText("Stopped");
    fireEvent.click(screen.getByText(/Click to browse or drag a file here/));
    await screen.findByText("Honda.pdf");

    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    await waitFor(() => expect(apiPost).toHaveBeenCalledWith(
      "/api/ingestion/jobs",
      expect.objectContaining({
        documentType: "manual-pdf",
        configuration: { extractGraphRelationships: true, ocrEnabled: true },
      }),
    ));
    expect(queueLocalIngestionWorkItem).toHaveBeenCalledWith(expect.objectContaining({
      sourcePath: "/manuals/Honda.pdf",
      sourceFileName: "Honda.pdf",
    }));
  });

  it("blocks queueing when the upload secret is missing", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockResolvedValue("/data/specs.csv");
    renderScreen();
    await screen.findByText("Stopped");
    fireEvent.click(screen.getByText(/Click to browse or drag a file here/));
    await screen.findByText("specs.csv");

    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    expect(await screen.findByText(/Upload job secret is required/)).toBeInTheDocument();
    expect(apiPost).not.toHaveBeenCalled();
  });

  it("shows and dismisses processor stop errors", async () => {
    configureCloudJobs([]);
    isListening.mockResolvedValue(true);
    health.mockResolvedValue({
      status: "healthy",
      accepting_work: true,
      api_client_configured: true,
    });
    processorStop.mockRejectedValue(new Error("shutdown denied"));
    renderScreen();
    await screen.findByText("Running");

    fireEvent.click(screen.getByRole("button", { name: /^stop$/i }));

    const message = await screen.findByText(/Could not stop the processor:/);
    fireEvent.click(message.parentElement!.querySelector("button")!);
    await waitFor(() =>
      expect(screen.queryByText(/Could not stop the processor:/)).not.toBeInTheDocument(),
    );
  });

  it("accepts a dropped CSV and queues CSV-specific ingestion", async () => {
    configureCloudJobs([]);
    getTauriFilePath.mockReturnValue("/data/specs.csv");
    apiPost.mockResolvedValue({
      data: makeJob({
        status: "Queued",
        inputType: "spec-dataset",
        inputRef: "/data/specs.csv",
      }),
    });
    renderScreen({ pythonUploadJobSecret: "secret" });
    await screen.findByText("Stopped");
    const dropZone = screen.getByText(/Click to browse or drag a file here/).parentElement!;

    fireEvent.dragOver(dropZone);
    fireEvent.drop(dropZone, {
      dataTransfer: { files: [new File(["a,b"], "specs.csv")] },
    });
    expect(await screen.findByText("specs.csv")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    await waitFor(() =>
      expect(apiPost).toHaveBeenCalledWith(
        "/api/ingestion/jobs",
        expect.objectContaining({
          documentType: "spec-dataset",
          configuration: undefined,
        }),
      ),
    );
    expect(queueLocalIngestionWorkItem).toHaveBeenCalledWith(
      expect.objectContaining({
        sourcePath: "/data/specs.csv",
        sourceFileName: "specs.csv",
        documentType: "spec-dataset",
      }),
    );
  });

  it("rejects queueing when a running processor lacks API configuration", async () => {
    configureCloudJobs([]);
    isListening.mockResolvedValue(true);
    health.mockResolvedValue({
      status: "healthy",
      accepting_work: true,
      api_client_configured: false,
    });
    pickLocalIngestionFile.mockResolvedValue("/manuals/Honda.pdf");
    renderScreen({ pythonUploadJobSecret: "secret" });
    await screen.findByText("Running");
    fireEvent.click(screen.getByText(/Click to browse or drag a file here/));
    await screen.findByText("Honda.pdf");

    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    expect(
      await screen.findByText(/was started without the upload job secret/),
    ).toBeInTheDocument();
    expect(apiPost).not.toHaveBeenCalled();
  });

  it("cleans up the cloud job when local queueing fails", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockResolvedValue("/manuals/Honda.pdf");
    apiPost.mockResolvedValue({
      data: makeJob({ status: "Queued", jobId: "job-cleanup" }),
    });
    queueLocalIngestionWorkItem.mockRejectedValue(
      new Error("watch folder unavailable"),
    );
    renderScreen({ pythonUploadJobSecret: "secret" });
    await screen.findByText("Stopped");
    fireEvent.click(screen.getByText(/Click to browse or drag a file here/));
    await screen.findByText("Honda.pdf");

    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    expect(
      await screen.findByText(/watch folder unavailable/),
    ).toBeInTheDocument();
    expect(apiDelete).toHaveBeenCalledWith(
      "/api/ingestion/jobs/job-cleanup",
    );
  });

  it("reports a cloud job that immediately fails to start", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockResolvedValue("/manuals/Honda.pdf");
    apiPost.mockResolvedValue({
      data: makeJob({
        status: "Failed",
        jobId: "job-failed",
        failureReason: "Invalid document",
        docIngestionRunId: "run-failed",
      }),
    });
    renderScreen({ pythonUploadJobSecret: "secret" });
    await screen.findByText("Stopped");
    fireEvent.click(screen.getByText(/Click to browse or drag a file here/));
    await screen.findByText("Honda.pdf");

    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    expect(await screen.findByText(/Invalid document/)).toBeInTheDocument();
    expect(queueLocalIngestionWorkItem).not.toHaveBeenCalled();
  });

  it("stops an active cloud and local processor job", async () => {
    configureCloudJobs([
      makeJob({
        id: 80,
        jobId: "job-80",
        status: "Processing",
        currentStage: "embedding",
        docIngestionRunId: "run-80",
        indexedChunkCount: 3,
        expectedChunkCount: 8,
      }),
    ]);
    isListening.mockResolvedValue(true);
    health.mockResolvedValue({
      status: "healthy",
      accepting_work: true,
      api_client_configured: true,
    });
    renderScreen();
    await screen.findByText("Job 80");
    expect(screen.getByText("3/8 chunks")).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole("button", { name: "Stop" })[1]);

    await waitFor(() =>
      expect(stopJob).toHaveBeenCalledWith("run-80", 8100),
    );
    expect(apiPost).toHaveBeenCalledWith(
      "/api/ingestion/jobs/job-80/cancel",
    );
  });

  it("retries failed jobs and deletes completed jobs", async () => {
    configureCloudJobs([
      makeJob({
        id: 81,
        jobId: "job-81",
        status: "Failed",
        failureReason: "bad input",
      }),
      makeJob({ id: 82, jobId: "job-82", status: "Completed" }),
    ]);
    apiPost.mockResolvedValue({
      data: makeJob({ id: 83, jobId: "job-83", status: "Queued" }),
    });
    renderScreen();
    await screen.findByText("Job 81");

    fireEvent.click(screen.getAllByRole("button", { name: "Delete" })[1]);
    await waitFor(() =>
      expect(apiDelete).toHaveBeenCalledWith(
        "/api/ingestion/jobs/job-82",
      ),
    );

    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    await waitFor(() =>
      expect(apiPost).toHaveBeenCalledWith(
        "/api/ingestion/jobs/job-81/retry",
      ),
    );
  });

  it("submits manual metadata and dismisses the success banner", async () => {
    configureCloudJobs([
      makeJob({
        id: 84,
        jobId: "job-84",
        status: "AwaitingMetadata",
      }),
    ]);
    renderScreen();
    await screen.findByRole("dialog");
    fireEvent.change(screen.getByLabelText("Manual metadata JSON"), {
      target: {
        value: JSON.stringify({
          make: "Honda",
          model: "CBR600RR",
          year: 2023,
          category: "sport",
        }),
      },
    });

    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    const success = await screen.findByText(
      "Metadata submitted. Pipeline resuming.",
    );
    expect(apiPost).toHaveBeenCalledWith(
      "/api/ingestion/jobs/job-84/metadata",
      expect.objectContaining({
        metadataJson: expect.stringContaining('"make":"Honda"'),
      }),
    );
    fireEvent.click(success.parentElement!.querySelector("button")!);
    await waitFor(() =>
      expect(
        screen.queryByText("Metadata submitted. Pipeline resuming."),
      ).not.toBeInTheDocument(),
    );
  });

  it("reports a stale cloud job when the ready local processor no longer has its run", async () => {
    const staleJob = makeJob({
      id: 90,
      jobId: "job-stale",
      status: "Processing",
      docIngestionRunId: "run-stale",
      createdAtUtc: new Date(Date.now() - 31_000).toISOString(),
    });
    configureCloudJobs([staleJob]);
    isListening.mockResolvedValue(true);
    health.mockResolvedValue({
      status: "ok",
      accepting_work: true,
      api_client_configured: true,
    });
    processorJobs.mockResolvedValue([]);
    renderScreen();

    await waitFor(() =>
      expect(apiPost).toHaveBeenCalledWith(
        "/api/ingestion/jobs/job-stale/fail",
        null,
        { params: { reason: "Local processor lost the job (stale)" } },
      ),
    );
  });

  it("rejects unsafe source paths and cleans up the created cloud job", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockResolvedValue("../etc/manual.pdf");
    apiPost.mockResolvedValue({
      data: makeJob({ status: "Queued", jobId: "job-unsafe" }),
    });
    const deleteFailure = vi.spyOn(console, "error").mockImplementation(() => undefined);
    apiDelete.mockRejectedValue(new Error("cleanup failed"));
    renderScreen({ pythonUploadJobSecret: "secret" });
    await screen.findByText("Stopped");

    fireEvent.click(screen.getByText(/Click to browse or drag a file here/));
    await screen.findByText("manual.pdf");
    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    expect(await screen.findByText(/Invalid file path detected/)).toBeInTheDocument();
    expect(queueLocalIngestionWorkItem).not.toHaveBeenCalled();
    expect(apiDelete).toHaveBeenCalledWith("/api/ingestion/jobs/job-unsafe");
    expect(deleteFailure).toHaveBeenCalledWith(
      "Failed to clean up job after queue failure:",
      expect.anything(),
    );
    deleteFailure.mockRestore();
  });

  it("deletes an active job after its cancellation reaches a terminal cloud status", async () => {
    const activeJob = makeJob({
      id: 91,
      jobId: "job-delete-active",
      status: "Processing",
      docIngestionRunId: "run-delete-active",
    });
    apiGet.mockImplementation(async (url: string) => {
      if (url.includes("upload-constraints")) {
        return { data: { maxFileSizeBytes: 104_857_600, supportedExtensions: [".pdf", ".csv"] } };
      }
      if (url === "/api/ingestion/jobs") return { data: [activeJob] };
      if (url === "/api/ingestion/jobs/job-delete-active") {
        return { data: { ...activeJob, status: "Cancelled" } };
      }
      return { data: {} };
    });
    apiPost.mockResolvedValue({ data: {} });
    apiDelete.mockResolvedValue({ data: {} });
    renderScreen();
    await screen.findByText("Job 91");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() => {
      expect(apiPost).toHaveBeenCalledWith("/api/ingestion/jobs/job-delete-active/cancel");
      expect(apiDelete).toHaveBeenCalledWith("/api/ingestion/jobs/job-delete-active");
    });
  });

  it("shows an actionable error when stopping a local processor run fails", async () => {
    configureCloudJobs([
      makeJob({
        id: 92,
        jobId: "job-stop-error",
        status: "Processing",
        docIngestionRunId: "run-stop-error",
      }),
    ]);
    isListening.mockResolvedValue(true);
    health.mockResolvedValue({
      status: "healthy",
      accepting_work: true,
      api_client_configured: true,
    });
    stopJob.mockRejectedValue(new Error("local stop failed"));
    renderScreen();
    await screen.findByText("Job 92");

    fireEvent.click(screen.getAllByRole("button", { name: "Stop" })[1]);

    expect(await screen.findByText(/local stop failed/)).toBeInTheDocument();
    expect(apiPost).not.toHaveBeenCalledWith("/api/ingestion/jobs/job-stop-error/cancel");
  });

  it("shows the cloud-jobs query failure instead of an empty result", async () => {
    apiGet.mockImplementation(async (url: string) => {
      if (url.includes("upload-constraints")) {
        return { data: { maxFileSizeBytes: 104_857_600, supportedExtensions: [".pdf", ".csv"] } };
      }
      if (url === "/api/ingestion/jobs") throw new Error("cloud unavailable");
      return { data: {} };
    });
    renderScreen();

    expect(await screen.findByText(/Could not load jobs:/)).toBeInTheDocument();
  });

  it("keeps the selection empty when the picker is cancelled and reports a dropped-path conversion failure", async () => {
    configureCloudJobs([]);
    pickLocalIngestionFile.mockResolvedValue(null);
    getTauriFilePath.mockImplementation(() => {
      throw new Error("path conversion failed");
    });
    renderScreen();
    await screen.findByText("Stopped");
    const dropZone = screen.getByText(/Click to browse or drag a file here/).parentElement!;

    fireEvent.click(dropZone);
    await waitFor(() => expect(pickLocalIngestionFile).toHaveBeenCalledOnce());
    expect(screen.getByText(/Click to browse or drag a file here/)).toBeInTheDocument();

    fireEvent.drop(dropZone, { dataTransfer: { files: [new File(["pdf"], "manual.pdf")] } });
    expect(await screen.findByText("path conversion failed")).toBeInTheDocument();
  });
});
