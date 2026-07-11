import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import ChatInterface from "./ChatInterface";

describe("ChatInterface", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
    Element.prototype.scrollIntoView = vi.fn();
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders the welcome message and ignores empty submits", async () => {
    render(<ChatInterface />);
    expect(screen.getByText(/Motorcycle RAG Agent/i)).toBeInTheDocument();
    expect(screen.getByText(/Model:/i)).toBeInTheDocument();

    fireEvent.submit(screen.getByPlaceholderText(/Type your motorcycle query/i).closest("form")!);
    expect(fetch).not.toHaveBeenCalled();
  });

  it("sends a query and renders the assistant response with actions", async () => {
    vi.mocked(fetch).mockResolvedValue({
      ok: true,
      json: async () => ({
        response: "Use 10W-40.",
        modelUsed: "gpt-test",
        suggestions: [
          { label: "More specs", query: "more specs", reason: "detail", subject: "oil" },
        ],
        sources: [{ id: "1" }],
      }),
    } as Response);

    render(<ChatInterface />);
    const input = screen.getByPlaceholderText(/Type your motorcycle query/i);
    fireEvent.change(input, { target: { value: "  What oil?  " } });
    fireEvent.submit(input.closest("form")!);

    expect(await screen.findByText("Use 10W-40.")).toBeInTheDocument();
    expect(screen.getByText("gpt-test")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /More specs/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /SOURCES/i })).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(
      "/api/motorcycles/query",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("shows loading state while the request is in flight", async () => {
    let resolveFetch!: (value: Response) => void;
    vi.mocked(fetch).mockReturnValue(
      new Promise<Response>((resolve) => {
        resolveFetch = resolve;
      }),
    );

    render(<ChatInterface />);
    const input = screen.getByPlaceholderText(/Type your motorcycle query/i);
    fireEvent.change(input, { target: { value: "loading check" } });
    fireEvent.submit(input.closest("form")!);

    expect(await screen.findByText("Analysis in progress...")).toBeInTheDocument();

    resolveFetch({
      ok: true,
      json: async () => ({ response: "loaded" }),
    } as Response);
    expect(await screen.findByText("loaded")).toBeInTheDocument();
  });

  it("shows a connection error when the API fails", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    vi.mocked(fetch).mockResolvedValue({ ok: false, status: 500 } as Response);

    render(<ChatInterface />);
    fireEvent.change(screen.getByPlaceholderText(/Type your motorcycle query/i), {
      target: { value: "fail please" },
    });
    fireEvent.submit(screen.getByPlaceholderText(/Type your motorcycle query/i).closest("form")!);

    expect(
      await screen.findByText(/error connecting to the motorcycle database/i),
    ).toBeInTheDocument();
    expect(error).toHaveBeenCalled();
  });

  it("shows a timeout message when the request is aborted", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    vi.mocked(fetch).mockRejectedValue(new DOMException("Aborted", "AbortError"));

    render(<ChatInterface />);
    fireEvent.change(screen.getByPlaceholderText(/Type your motorcycle query/i), {
      target: { value: "slow query" },
    });
    fireEvent.submit(screen.getByPlaceholderText(/Type your motorcycle query/i).closest("form")!);

    expect(await screen.findByText(/request timed out/i)).toBeInTheDocument();
    expect(error).toHaveBeenCalled();
  });

  it("sends a follow-up when a callback action is clicked", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          response: "First answer",
          suggestions: [{ label: "Follow up", query: "follow up query" }],
        }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ response: "Second answer" }),
      } as Response);

    render(<ChatInterface />);
    fireEvent.change(screen.getByPlaceholderText(/Type your motorcycle query/i), {
      target: { value: "first" },
    });
    fireEvent.submit(screen.getByPlaceholderText(/Type your motorcycle query/i).closest("form")!);

    const action = await screen.findByRole("button", { name: /Follow up/i });
    fireEvent.click(action);

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    expect(await screen.findByText("Second answer")).toBeInTheDocument();
  });

  it("does not send while a request is already loading", async () => {
    let resolveFetch!: (value: Response) => void;
    vi.mocked(fetch).mockReturnValue(
      new Promise<Response>((resolve) => {
        resolveFetch = resolve;
      }),
    );

    render(<ChatInterface />);
    const input = screen.getByPlaceholderText(/Type your motorcycle query/i);
    fireEvent.change(input, { target: { value: "first" } });
    fireEvent.submit(input.closest("form")!);

    await screen.findByText("Analysis in progress...");
    fireEvent.change(input, { target: { value: "second" } });
    fireEvent.submit(input.closest("form")!);
    expect(fetch).toHaveBeenCalledTimes(1);

    resolveFetch({
      ok: true,
      json: async () => ({ response: "done" }),
    } as Response);
    expect(await screen.findByText("done")).toBeInTheDocument();
  });
});
