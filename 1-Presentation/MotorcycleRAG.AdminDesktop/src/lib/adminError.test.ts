import { describe, expect, it } from "vitest";
import { formatAdminError } from "./adminError";

describe("formatAdminError", () => {
  it("explains cloud API network errors as Admin Desktop to API failures", () => {
    const message = formatAdminError(
      {
        isAxiosError: true,
        message: "Network Error",
        config: {
          method: "post",
          url: "/api/ingestion/jobs",
          baseURL: "https://motorag.api.palfery.com",
        },
      },
      {
        action: "Creating local PDF manual ingestion work item",
        kind: "cloud-api",
      },
    );

    expect(message).toContain("POST https://motorag.api.palfery.com/api/ingestion/jobs");
    expect(message).toContain("Transport error: Network Error.");
    expect(message).toContain("Admin Desktop -> cloud API connection");
    expect(message).toContain("does not identify the local processor or LM Studio");
  });

  it("includes HTTP status and trace details for ProblemDetails responses", () => {
    const message = formatAdminError(
      {
        isAxiosError: true,
        message: "Request failed with status code 500",
        config: {
          method: "get",
          url: "/api/ingestion/jobs",
          baseURL: "https://motorag.api.palfery.com",
        },
        response: {
          status: 500,
          data: {
            detail: "Failed to start ingestion job.",
            traceId: "trace-123",
            referenceId: "ref-456",
          },
        },
      },
      {
        action: "Loading ingestion jobs",
        kind: "cloud-api",
      },
    );

    expect(message).toContain("HTTP 500");
    expect(message).toContain("Failed to start ingestion job.");
    expect(message).toContain("trace: trace-123");
    expect(message).toContain("reference: ref-456");
  });

  it("labels endpoint-backed local processor failures with the local endpoint", () => {
    const message = formatAdminError(new Error("The running local processor is not accepting work."), {
      action: "Loading local processor jobs",
      kind: "local-processor",
      localProcessorPort: 8100,
      location: "endpoint",
    });

    expect(message).toContain("Loading local processor jobs failed at the local processor endpoint");
    expect(message).toContain("127.0.0.1:8100");
  });

  it("keeps non-endpoint local processor failures scoped to the desktop integration", () => {
    const message = formatAdminError(new Error("Failed to spawn python process."), {
      action: "Starting the local processor",
      kind: "local-processor",
      localProcessorPort: 8100,
      location: "integration",
    });

    expect(message).toContain("Starting the local processor failed in the desktop app's local processor integration");
    expect(message).not.toContain("127.0.0.1:8100");
  });
});
