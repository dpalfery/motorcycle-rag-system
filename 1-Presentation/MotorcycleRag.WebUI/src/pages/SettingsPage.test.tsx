import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import SettingsPage from "./SettingsPage";

function makeFile(name: string, size = 1024 * 1024) {
  return new File(["x".repeat(size)], name, { type: "application/octet-stream" });
}

function dropFile(zone: HTMLElement, file: File) {
  fireEvent.dragEnter(zone, {
    dataTransfer: { files: [file], items: [], types: ["Files"] },
  });
  fireEvent.dragOver(zone, {
    dataTransfer: { files: [file], items: [], types: ["Files"] },
  });
  fireEvent.drop(zone, {
    dataTransfer: { files: [file], items: [], types: ["Files"] },
  });
}

describe("SettingsPage", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders seeded uploads and toggles drag active state", () => {
    render(<SettingsPage />);
    expect(screen.getByText("System Configuration")).toBeInTheDocument();
    expect(screen.getByText("2024_YZF_R1_Manual.pdf")).toBeInTheDocument();
    expect(screen.getByText("parts_catalog_v2.csv")).toBeInTheDocument();
    expect(screen.getByText("Indexed")).toBeInTheDocument();
    expect(screen.getByText("Processing")).toBeInTheDocument();

    const zone = screen.getByText(/Drag & Drop files here/i).closest("div.group")!;
    fireEvent.dragEnter(zone);
    expect(zone.className).toContain("border-primary");
    fireEvent.dragLeave(zone);
    expect(zone.className).not.toMatch(/border-primary bg-primary/);
  });

  it("records an error for unsupported file types", async () => {
    render(<SettingsPage />);
    const zone = screen.getByText(/Drag & Drop files here/i).closest("div.group")!;
    dropFile(zone, makeFile("notes.txt"));

    await waitFor(() => expect(screen.getByText("notes.txt")).toBeInTheDocument());
    expect(screen.getByText("Error")).toBeInTheDocument();
    expect(screen.getByTitle(/Only PDF manuals and CSV/i)).toBeInTheDocument();
  });

  it("uploads a PDF and starts processing with OCR config", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          uploadId: "up-1",
          fileName: "manual.pdf",
          documentType: "manual-pdf",
          status: "uploaded",
        }),
      } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({}) } as Response);

    render(<SettingsPage />);
    const zone = screen.getByText(/Drag & Drop files here/i).closest("div.group")!;
    dropFile(zone, makeFile("manual.pdf"));

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    expect(fetch).toHaveBeenNthCalledWith(
      1,
      "/api/ingestion/jobs/upload?documentType=manual-pdf",
      expect.objectContaining({ method: "POST" }),
    );
    expect(fetch).toHaveBeenNthCalledWith(
      2,
      "/api/ingestion/jobs",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          uploadId: "up-1",
          documentType: "manual-pdf",
          configuration: {
            extractGraphRelationships: true,
            ocrEnabled: true,
          },
        }),
      }),
    );
    expect(screen.getByText("manual.pdf")).toBeInTheDocument();
  });

  it("uploads a CSV without start configuration", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          uploadId: "up-2",
          fileName: "specs.csv",
          documentType: "spec-dataset",
          status: "uploaded",
        }),
      } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({}) } as Response);

    render(<SettingsPage />);
    dropFile(
      screen.getByText(/Drag & Drop files here/i).closest("div.group")!,
      makeFile("specs.csv"),
    );

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    const startBody = JSON.parse(
      (vi.mocked(fetch).mock.calls[1][1] as RequestInit).body as string,
    );
    expect(startBody.configuration).toBeUndefined();
  });

  it("surfaces upload failures with problem details", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      json: async () => ({
        title: "Upload rejected",
        detail: "File too large",
        traceId: "trace-1",
        referenceId: "ref-1",
      }),
    } as Response);

    render(<SettingsPage />);
    dropFile(
      screen.getByText(/Drag & Drop files here/i).closest("div.group")!,
      makeFile("manual.pdf"),
    );

    await waitFor(() =>
      expect(
        screen.getByTitle(/File too large \(trace: trace-1, reference: ref-1\)/),
      ).toBeInTheDocument(),
    );
    expect(error).toHaveBeenCalled();
  });

  it("surfaces start-job failures using extension ids and non-json bodies", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    vi.mocked(fetch)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          uploadId: "up-3",
          fileName: "manual.pdf",
          documentType: "manual-pdf",
          status: "uploaded",
        }),
      } as Response)
      .mockResolvedValueOnce({
        ok: false,
        json: async () => ({
          title: "Start failed",
          extensions: { traceId: "t-2", referenceId: "r-2" },
        }),
      } as Response);

    render(<SettingsPage />);
    dropFile(
      screen.getByText(/Drag & Drop files here/i).closest("div.group")!,
      makeFile("manual.pdf"),
    );

    await waitFor(() =>
      expect(screen.getByTitle(/Start failed \(trace: t-2, reference: r-2\)/)).toBeInTheDocument(),
    );

    cleanup();
    vi.mocked(fetch).mockReset();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      json: async () => {
        throw new Error("not json");
      },
    } as Response);

    render(<SettingsPage />);
    dropFile(
      screen.getByText(/Drag & Drop files here/i).closest("div.group")!,
      makeFile("data.csv"),
    );

    await waitFor(() =>
      expect(
        screen.getByTitle(/The source file could not be uploaded/),
      ).toBeInTheDocument(),
    );
    expect(error).toHaveBeenCalled();
  });

  it("stringifies non-Error upload failures", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    vi.mocked(fetch).mockRejectedValue("network down");

    render(<SettingsPage />);
    dropFile(
      screen.getByText(/Drag & Drop files here/i).closest("div.group")!,
      makeFile("manual.pdf"),
    );

    await waitFor(() => expect(screen.getByTitle("network down")).toBeInTheDocument());
    expect(error).toHaveBeenCalled();
  });
});
