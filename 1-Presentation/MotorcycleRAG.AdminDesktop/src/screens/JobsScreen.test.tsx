import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import JobsScreen from "./JobsScreen";
import { useConfig, DEFAULT_CONFIG } from "@/lib/config";
import type { IngestionJobStatus } from "@/lib/ingestionJob";

// `vi.hoisted` runs before imports so the mock factories can reference the stubs.
const { apiGet, apiPost, apiDelete } = vi.hoisted(() => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiDelete: vi.fn(),
}));
const { processorIsListening, processorStopJob } = vi.hoisted(() => ({
  processorIsListening: vi.fn(),
  processorStopJob: vi.fn(),
}));

vi.mock("@/lib/apiClient", () => ({
  api: { get: apiGet, post: apiPost, delete: apiDelete },
}));
vi.mock("@/lib/processor", () => ({
  isMissingProcessorJobError: (error: unknown) =>
    String(error).toLowerCase().includes("job not found"),
  processor: {
    isListening: processorIsListening,
    stopJob: processorStopJob,
    resumeProcessPdf: vi.fn(),
  },
}));

/** Builds a minimal IngestionJobStatus; defaults to an awaiting-metadata PDF job. */
function makeJob(overrides: Partial<IngestionJobStatus> = {}): IngestionJobStatus {
  return {
    jobId: "job-1",
    id: 1,
    status: "AwaitingMetadata",
    createdAtUtc: new Date().toISOString(),
    inputType: "manual-pdf",
    inputRef: "blob://upload.pdf",
    ...overrides,
  };
}

/**
 * Programs the mocked cloud API for a jobs list. The jobs GET returns the array directly
 * (matching the screen's `Array.isArray(res.data)` branch); the metadata GET returns an
 * empty metadata response so the modal's pre-fill query resolves cleanly.
 */
function configureApiJobs(jobs: IngestionJobStatus[]) {
  apiGet.mockImplementation(async (url: string) => {
    if (url === "/api/ingestion/jobs") {
      return { data: jobs };
    }
    if (/\/api\/ingestion\/jobs\/[^/]+\/metadata$/.test(url)) {
      return {
        data: {
          jobId: jobs[0]?.jobId ?? "job-1",
          make: null,
          model: null,
          year: null,
          category: null,
          tags: [],
          fillRate: 0,
          isComplete: false,
        },
      };
    }
    return { data: {} };
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
  // Seed the Zustand config store with deterministic defaults (isolated per test).
  useConfig.setState({ config: { ...DEFAULT_CONFIG }, loaded: true });

  return render(
    <QueryClientProvider client={queryClient}>
      <JobsScreen />
    </QueryClientProvider>,
  );
}

describe("JobsScreen — Enter Metadata button", () => {
  beforeEach(() => {
    apiGet.mockReset();
    apiPost.mockReset();
    apiDelete.mockReset();
    processorIsListening.mockReset().mockResolvedValue(false);
    processorStopJob.mockReset().mockResolvedValue(undefined);
  });
  afterEach(() => cleanup());

  it("renders the Enter Metadata button for an AwaitingMetadata job", async () => {
    configureApiJobs([makeJob({ status: "AwaitingMetadata" })]);
    renderScreen();

    expect(await screen.findByRole("button", { name: "Enter Metadata" })).toBeInTheDocument();
  });

  it("does not render the Enter Metadata button for a completed job", async () => {
    configureApiJobs([makeJob({ id: 2, jobId: "job-2", status: "Completed" })]);
    renderScreen();

    await screen.findByText("Job 2");

    expect(
      screen.queryByRole("button", { name: "Enter Metadata" }),
    ).not.toBeInTheDocument();
  });

  it("does not render the Enter Metadata button for a failed job", async () => {
    configureApiJobs([
      makeJob({ id: 3, jobId: "job-3", status: "Failed", failureReason: "boom" }),
    ]);
    renderScreen();

    await screen.findByText("Job 3");

    expect(
      screen.queryByRole("button", { name: "Enter Metadata" }),
    ).not.toBeInTheDocument();
  });

  it("opens the ManualMetadataModal when the Enter Metadata button is clicked", async () => {
    configureApiJobs([makeJob({ status: "AwaitingMetadata" })]);
    renderScreen();

    const button = await screen.findByRole("button", { name: "Enter Metadata" });
    fireEvent.click(button);

    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("reopens the modal after dismissing it and clicking the button again", async () => {
    configureApiJobs([makeJob({ status: "AwaitingMetadata" })]);
    renderScreen();

    // A job is awaiting metadata, so the modal auto-opens on first render.
    expect(await screen.findByRole("dialog")).toBeInTheDocument();

    // Dismiss the modal via Cancel.
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    // Re-open it from the row action button.
    fireEvent.click(screen.getByRole("button", { name: "Enter Metadata" }));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("surfaces a clickable Enter Metadata entry in the failure panel for a manual-metadata failure and opens the modal on click", async () => {
    // A failed job (panel renders) flagged by the backend as requiring manual metadata.
    configureApiJobs([
      makeJob({
        id: 20,
        jobId: "job-20",
        status: "Failed",
        failureDetail: "Could not determine make and model.",
        requiresManualMetadata: true,
      }),
    ]);
    renderScreen();

    const panelButton = await screen.findByRole("button", { name: "Enter Metadata →" });
    fireEvent.click(panelButton);

    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });
});

describe("JobsScreen — source file name subtitle", () => {
  beforeEach(() => {
    apiGet.mockReset();
    apiPost.mockReset();
    apiDelete.mockReset();
    processorIsListening.mockReset().mockResolvedValue(false);
    processorStopJob.mockReset().mockResolvedValue(undefined);
  });
  afterEach(() => cleanup());

  it("renders sourceFileName as a subtitle when present", async () => {
    configureApiJobs([
      makeJob({
        id: 10,
        jobId: "job-10",
        status: "Completed",
        sourceFileName: "Honda-CBR600RR-2023.pdf",
      }),
    ]);
    renderScreen();

    await screen.findByText("Job 10");

    expect(screen.getByText("Honda-CBR600RR-2023.pdf")).toBeInTheDocument();
  });

  it("does not render a subtitle when sourceFileName is undefined", async () => {
    configureApiJobs([
      makeJob({ id: 11, jobId: "job-11", status: "Completed", sourceFileName: undefined }),
    ]);
    renderScreen();

    await screen.findByText("Job 11");

    // No file-name subtitle should be present anywhere on the screen.
    expect(screen.queryByText(/\.pdf$/i)).not.toBeInTheDocument();
  });

  it("renders only the basename value of sourceFileName", async () => {
    // The IngestionJobStatus.sourceFileName contract is basename-only (the data layer
    // strips directory paths before populating it). The component surfaces the value
    // verbatim, so passing a basename verifies the subtitle shows just the file name.
    configureApiJobs([
      makeJob({
        id: 12,
        jobId: "job-12",
        status: "Completed",
        sourceFileName: "owner-manual.pdf",
      }),
    ]);
    renderScreen();

    await screen.findByText("Job 12");

    const subtitle = screen.getByText("owner-manual.pdf");
    expect(subtitle).toBeInTheDocument();
    // The subtitle's title attribute carries the same value (used for the hover tooltip).
    expect(subtitle).toHaveAttribute("title", "owner-manual.pdf");
  });

  it("renders metadata fill rates and active job totals", async () => {
    configureApiJobs([
      makeJob({
        id: 30,
        jobId: "job-30",
        status: "Processing",
        make: "Honda",
        model: "CBR600RR",
        year: 2023,
        category: "sport",
        fillRate: 0.42,
      }),
      makeJob({
        id: 31,
        jobId: "job-31",
        status: "Queued",
        make: "Yamaha",
        model: "MT-07",
        fillRate: 0.75,
      }),
    ]);
    renderScreen();

    expect(await screen.findByText(/Honda CBR600RR/)).toBeInTheDocument();
    expect(screen.getByText("42% complete")).toBeInTheDocument();
    expect(screen.getByText("75% complete")).toBeInTheDocument();
    expect(screen.getByText("Active").nextElementSibling).toHaveTextContent("2");
  });

  it("retries failed jobs and replaces a superseded job", async () => {
    configureApiJobs([
      makeJob({ id: 40, jobId: "job-40", status: "Failed", failureReason: "failed" }),
    ]);
    apiPost.mockResolvedValue({
      data: makeJob({ id: 41, jobId: "job-41", status: "Queued" }),
    });
    renderScreen();
    await screen.findByText("Job 40");

    fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(apiPost).toHaveBeenCalledWith(
      "/api/ingestion/jobs/job-40/retry",
    ));
  });

  it("deletes completed jobs and refreshes the list", async () => {
    configureApiJobs([
      makeJob({ id: 50, jobId: "job-50", status: "Completed" }),
    ]);
    renderScreen();
    await screen.findByText("Job 50");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(apiDelete).toHaveBeenCalledWith(
      "/api/ingestion/jobs/job-50",
    ));

    fireEvent.click(screen.getByRole("button", { name: /refresh/i }));
    await waitFor(() => expect(apiGet.mock.calls.filter(
      ([url]) => url === "/api/ingestion/jobs",
    ).length).toBeGreaterThan(1));
  });

  it("shows conflict details when deletion is already underway", async () => {
    configureApiJobs([
      makeJob({ id: 60, jobId: "job-60", status: "Completed" }),
    ]);
    apiDelete.mockRejectedValue({
      isAxiosError: true,
      response: {
        status: 409,
        data: { detail: "Deletion is already underway." },
      },
    });
    renderScreen();
    await screen.findByText("Job 60");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    expect(await screen.findByText("Deletion is already underway.")).toBeInTheDocument();
  });

  it("renders empty and cloud API error states", async () => {
    configureApiJobs([]);
    const first = renderScreen();
    expect(await screen.findByText("No jobs found.")).toBeInTheDocument();
    first.unmount();

    apiGet.mockReset().mockRejectedValue(new Error("API offline"));
    renderScreen();
    expect(await screen.findByText(/Could not load jobs: API offline/)).toBeInTheDocument();
  });

  it("cancels an active job before deleting it", async () => {
    const active = makeJob({
      id: 70,
      jobId: "job-70",
      status: "Processing",
      docIngestionRunId: "run-70",
    });
    configureApiJobs([active]);
    apiGet.mockImplementation(async (url: string) => {
      if (url === "/api/ingestion/jobs") return { data: [active] };
      if (url === "/api/ingestion/jobs/job-70") {
        return { data: { ...active, status: "Cancelled" } };
      }
      return { data: {} };
    });
    renderScreen();
    await screen.findByText("Job 70");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() =>
      expect(apiPost).toHaveBeenCalledWith(
        "/api/ingestion/jobs/job-70/cancel",
      ),
    );
    expect(processorStopJob).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(apiDelete).toHaveBeenCalledWith("/api/ingestion/jobs/job-70"),
    );
  });

  it("stops a local processor run before cancelling an active job", async () => {
    const active = makeJob({
      id: 71,
      jobId: "job-71",
      status: "Processing",
      inputType: "manual-pdf",
      docIngestionRunId: "run-71",
      computeProvider: "LocalProcessor",
    });
    configureApiJobs([active]);
    processorIsListening.mockResolvedValue(true);
    apiGet.mockImplementation(async (url: string) => {
      if (url === "/api/ingestion/jobs") return { data: [active] };
      if (url === "/api/ingestion/jobs/job-71") {
        return { data: { ...active, status: "Cancelled" } };
      }
      return { data: {} };
    });
    renderScreen();
    await screen.findByText("Job 71");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() =>
      expect(processorStopJob).toHaveBeenCalledWith("run-71", 8100),
    );
    await waitFor(() =>
      expect(apiDelete).toHaveBeenCalledWith("/api/ingestion/jobs/job-71"),
    );
  });

  it("shows and dismisses ordinary deletion failures", async () => {
    configureApiJobs([
      makeJob({ id: 72, jobId: "job-72", status: "Completed" }),
    ]);
    apiDelete.mockRejectedValue(new Error("permission denied"));
    renderScreen();
    await screen.findByText("Job 72");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    const message = await screen.findByText("permission denied");
    fireEvent.click(message.parentElement!.querySelector("button")!);
    await waitFor(() =>
      expect(screen.queryByText("permission denied")).not.toBeInTheDocument(),
    );
  });

  it("continues deleting when the local processor has already forgotten the active run", async () => {
    const active = makeJob({
      id: 73,
      jobId: "job-73",
      status: "Processing",
      docIngestionRunId: "run-73",
      computeProvider: "LocalProcessor",
    });
    configureApiJobs([active]);
    processorIsListening.mockResolvedValue(true);
    processorStopJob.mockRejectedValue(new Error("job not found"));
    apiGet.mockImplementation(async (url: string) => {
      if (url === "/api/ingestion/jobs") return { data: [active] };
      if (url === "/api/ingestion/jobs/job-73") {
        return { data: { ...active, status: "Cancelled" } };
      }
      return { data: {} };
    });
    const warning = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    renderScreen();
    await screen.findByText("Job 73");

    fireEvent.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() =>
      expect(apiDelete).toHaveBeenCalledWith("/api/ingestion/jobs/job-73"),
    );
    expect(warning).toHaveBeenCalledWith(
      "Per-job local processor stop skipped because the run is no longer present:",
      "run-73",
    );
  });

  it("submits valid manual metadata, announces success, and lets the operator dismiss it", async () => {
    const job = makeJob({ id: 80, jobId: "job-80", status: "AwaitingMetadata" });
    configureApiJobs([job]);
    renderScreen();

    await screen.findByRole("dialog");
    const metadataJson = JSON.stringify({
      make: "Honda",
      model: "CBR600RR",
      year: 2023,
      category: "sport",
      tags: ["600cc"],
    });
    fireEvent.change(screen.getByLabelText("Manual metadata JSON"), {
      target: { value: metadataJson },
    });
    fireEvent.click(screen.getByRole("button", { name: "Submit" }));

    await waitFor(() =>
      expect(apiPost).toHaveBeenCalledWith(
        "/api/ingestion/jobs/job-80/metadata",
        { metadataJson },
      ),
    );
    const success = await screen.findByText("Metadata submitted. Pipeline resuming.");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    fireEvent.click(success.parentElement!.querySelector("button")!);
    await waitFor(() =>
      expect(screen.queryByText("Metadata submitted. Pipeline resuming.")).not.toBeInTheDocument(),
    );
  });

  it("keeps the manual metadata dialog open and surfaces a formatted submission failure", async () => {
    const job = makeJob({ id: 81, jobId: "job-81", status: "AwaitingMetadata" });
    configureApiJobs([job]);
    apiPost.mockRejectedValue(new Error("metadata service unavailable"));
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

    expect(await screen.findByRole("alert")).toHaveTextContent("metadata service unavailable");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("surfaces the reason when a retry is rejected and restores the failed badge", async () => {
    // Regression: the retry mutation had no onError. A rejected retry left only the
    // optimistic "queued" flash before onSettled refetched the still-failed job —
    // the reported "the page just blinks" with no explanation.
    const failedJob = makeJob({ id: 34, jobId: "job-34", status: "Failed" });
    configureApiJobs([failedJob]);
    apiPost.mockRejectedValue(
      Object.assign(new Error("Request failed with status code 409"), {
        isAxiosError: true,
        response: {
          status: 409,
          data: {
            title: "Job retry rejected",
            detail:
              "The source file for this ingestion job ('upload-34/source.pdf') was not found in blob storage.",
          },
        },
      }),
    );
    renderScreen();

    fireEvent.click(await screen.findByRole("button", { name: "Retry" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "was not found in blob storage",
    );
    // The badge must not be left on a "queued" state the server never accepted.
    await waitFor(() => expect(screen.getByText("Failed")).toBeInTheDocument());
    expect(screen.queryByText("queued")).not.toBeInTheDocument();
  });

  it("clears a previous retry error once a retry succeeds", async () => {
    const failedJob = makeJob({ id: 35, jobId: "job-35", status: "Failed" });
    configureApiJobs([failedJob]);
    apiPost.mockRejectedValueOnce(
      Object.assign(new Error("boom"), {
        isAxiosError: true,
        response: { status: 409, data: { detail: "cannot retry right now" } },
      }),
    );
    renderScreen();

    fireEvent.click(await screen.findByRole("button", { name: "Retry" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("cannot retry right now");

    apiPost.mockResolvedValue({ data: { ...failedJob, status: "queued" } });
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });

  it("renders an unknown status and an older creation time without metadata", async () => {
    configureApiJobs([
      makeJob({
        id: 82,
        jobId: "job-82",
        status: "WaitingForExternalService",
        createdAtUtc: new Date(Date.now() - 2 * 24 * 60 * 60 * 1_000).toISOString(),
      }),
    ]);
    renderScreen();

    expect(await screen.findByText("WaitingForExternalService")).toBeInTheDocument();
    expect(screen.getByText("2d ago")).toBeInTheDocument();
  });
});
