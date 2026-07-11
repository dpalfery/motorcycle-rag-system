import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import IngestionScreen from "./IngestionScreen";
import { DEFAULT_CONFIG, useConfig } from "@/lib/config";
import { renderWithQuery } from "@/test/renderWithQuery";

const {
  get,
  post,
  remove,
  getTauriFilePath,
  queueLocalIngestionWorkItem,
  isPathSafe,
} = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  remove: vi.fn(),
  getTauriFilePath: vi.fn(),
  queueLocalIngestionWorkItem: vi.fn(),
  isPathSafe: vi.fn(),
}));

vi.mock("@/lib/apiClient", () => ({
  api: { get, post, delete: remove },
}));
vi.mock("@/lib/localIngestion", () => ({
  getTauriFilePath,
  queueLocalIngestionWorkItem,
}));
vi.mock("@/lib/pathUtils", () => ({ isPathSafe }));

function job(overrides: Record<string, unknown> = {}) {
  return {
    jobId: "job-1",
    id: 1,
    status: "Completed",
    createdAtUtc: "2026-07-10T12:00:00Z",
    inputType: "manual-pdf",
    inputRef: "blob://manual.pdf",
    ...overrides,
  };
}

function configureApi(recent: unknown = []) {
  get.mockImplementation(async (url: string) => {
    if (url.includes("upload-constraints")) {
      return {
        data: {
          maxFileSizeBytes: 2 * 1024 * 1024,
          supportedExtensions: ["pdf", ".csv"],
        },
      };
    }
    if (url === "/api/ingestion/jobs?top=50") return { data: recent };
    if (url.startsWith("/api/ingestion/jobs/")) return { data: job() };
    return { data: {} };
  });
}

function chooseFile(container: HTMLElement, file: File) {
  const input = container.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [file] } });
}

describe("IngestionScreen", () => {
  beforeEach(() => {
    get.mockReset();
    post.mockReset();
    remove.mockReset();
    getTauriFilePath.mockReset();
    queueLocalIngestionWorkItem.mockReset();
    isPathSafe.mockReset();
    getTauriFilePath.mockReturnValue("/safe/manual.pdf");
    queueLocalIngestionWorkItem.mockResolvedValue(undefined);
    isPathSafe.mockReturnValue(true);
    remove.mockResolvedValue({ data: {} });
    useConfig.setState({ config: { ...DEFAULT_CONFIG }, loaded: true });
    configureApi();
  });

  afterEach(cleanup);

  it("shows constraints, empty history, and refreshes queries", async () => {
    renderWithQuery(<IngestionScreen />);

    expect(await screen.findByText(/\.pdf, \.csv/)).toBeInTheDocument();
    expect(screen.getByText(/max 2 MB/)).toBeInTheDocument();
    expect(await screen.findByText("No uploads yet.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(get.mock.calls.filter(
      ([url]) => url === "/api/ingestion/jobs?top=50",
    )).toHaveLength(2));
  });

  it("renders wrapped job history with progress, stage, and failure details", async () => {
    configureApi({
      items: [
        job({
          status: "Running",
          currentStage: "Embedding",
          stageSetAtUtc: "2026-07-10T12:01:00Z",
          indexedChunkCount: 3,
          expectedChunkCount: 10,
        }),
        job({
          jobId: "job-2",
          id: 2,
          status: "Failed",
          failureReason: "OCR failed",
        }),
      ],
    });
    renderWithQuery(<IngestionScreen />);

    expect(await screen.findByText("Stage: Embedding")).toBeInTheDocument();
    expect(screen.getByText("3/10 chunks")).toBeInTheDocument();
    expect(screen.getByText("Status: Failed")).toBeInTheDocument();
    expect(screen.getByText("OCR failed")).toBeInTheDocument();
  });

  it("shows loading and history errors", async () => {
    get.mockImplementation((url: string) => {
      if (url.includes("upload-constraints")) {
        return Promise.resolve({ data: { maxFileSizeBytes: 1024, supportedExtensions: [".pdf"] } });
      }
      return new Promise(() => undefined);
    });
    const first = renderWithQuery(<IngestionScreen />);
    expect(screen.getByText("Loading…")).toBeInTheDocument();
    first.unmount();

    get.mockImplementation((url: string) => {
      if (url.includes("upload-constraints")) {
        return Promise.resolve({ data: { maxFileSizeBytes: 1024, supportedExtensions: [".pdf"] } });
      }
      return Promise.reject(new Error("offline"));
    });
    renderWithQuery(<IngestionScreen />);
    expect(await screen.findByText("Could not load upload history.")).toBeInTheDocument();
  });

  it("rejects oversized and unsupported files", async () => {
    const alert = vi.spyOn(window, "alert").mockImplementation(() => undefined);
    const { container } = renderWithQuery(<IngestionScreen />);
    await screen.findByText(/max 2 MB/);

    const huge = new File(["x"], "huge.pdf");
    Object.defineProperty(huge, "size", { value: 3 * 1024 * 1024 });
    chooseFile(container, huge);
    expect(alert).toHaveBeenCalledWith("File exceeds the 2 MB limit.");

    chooseFile(container, new File(["text"], "notes.txt"));
    expect(alert).toHaveBeenCalledWith(
      "File type not supported. Allowed types: .pdf, .csv",
    );
    expect(screen.getByRole("button", { name: "Queue for local processing" })).toBeDisabled();
    alert.mockRestore();
  });

  it("queues a PDF work item and adds the submitted job", async () => {
    post.mockResolvedValue({
      data: job({ status: "Queued", docIngestionRunId: "run-1" }),
    });
    const file = new File(["pdf"], "manual.pdf", { type: "application/pdf" });
    getTauriFilePath.mockReturnValue("/safe/manual.pdf");
    const { container } = renderWithQuery(<IngestionScreen />);
    await screen.findByText(/\.pdf, \.csv/);

    chooseFile(container, file);
    expect(screen.getByText("manual.pdf")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(
      "/api/ingestion/jobs",
      expect.objectContaining({
        documentType: "manual-pdf",
        configuration: { extractGraphRelationships: true, ocrEnabled: true },
      }),
    ));
    expect(queueLocalIngestionWorkItem).toHaveBeenCalledWith(expect.objectContaining({
      sourcePath: "/safe/manual.pdf",
      jobId: "job-1",
      documentType: "manual-pdf",
      sourceFileName: "manual.pdf",
      size: file.size,
    }));
    await waitFor(() => expect(screen.queryByText("manual.pdf")).not.toBeInTheDocument());
  });

  it("queues CSV files without PDF start configuration", async () => {
    post.mockResolvedValue({ data: job({ status: "Pending" }) });
    const { container } = renderWithQuery(<IngestionScreen />);
    await screen.findByText(/\.pdf, \.csv/);

    chooseFile(container, new File(["a,b"], "specs.csv", { type: "text/csv" }));
    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(
      "/api/ingestion/jobs",
      expect.objectContaining({
        documentType: "spec-dataset",
        configuration: undefined,
      }),
    ));
  });

  it("reports cloud job-start failures without queueing local work", async () => {
    post.mockResolvedValue({
      data: job({
        status: "Failed",
        failureReason: "Pipeline rejected",
        docIngestionRunId: "pipeline-1",
      }),
    });
    const { container } = renderWithQuery(<IngestionScreen />);
    await screen.findByText(/\.pdf, \.csv/);
    chooseFile(container, new File(["pdf"], "manual.pdf"));
    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    expect(await screen.findByText(/Pipeline rejected/)).toBeInTheDocument();
    expect(screen.getByText(/Pipeline run ID: pipeline-1/)).toBeInTheDocument();
    expect(queueLocalIngestionWorkItem).not.toHaveBeenCalled();
  });

  it("deletes the cloud job when local queue validation fails", async () => {
    post.mockResolvedValue({ data: job({ status: "Queued" }) });
    isPathSafe.mockReturnValue(false);
    const { container } = renderWithQuery(<IngestionScreen />);
    await screen.findByText(/\.pdf, \.csv/);
    chooseFile(container, new File(["pdf"], "manual.pdf"));
    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    await waitFor(() => expect(remove).toHaveBeenCalledWith(
      "/api/ingestion/jobs/job-1",
    ));
    expect(await screen.findByText(/Invalid file path detected/)).toBeInTheDocument();
  });

  it("preserves the queue error when cleanup also fails", async () => {
    post.mockResolvedValue({ data: job({ status: "Queued" }) });
    queueLocalIngestionWorkItem.mockRejectedValue(new Error("queue unavailable"));
    remove.mockRejectedValue(new Error("delete unavailable"));
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const { container } = renderWithQuery(<IngestionScreen />);
    await screen.findByText(/\.pdf, \.csv/);
    chooseFile(container, new File(["a,b"], "specs.csv"));
    fireEvent.click(screen.getByRole("button", { name: "Queue for local processing" }));

    expect(await screen.findByText(/queue unavailable/)).toBeInTheDocument();
    expect(error).toHaveBeenCalledWith(
      "Failed to clean up job after queue failure:",
      expect.anything(),
    );
    error.mockRestore();
  });
});
