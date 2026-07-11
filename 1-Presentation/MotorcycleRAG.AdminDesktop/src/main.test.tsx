import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { waitFor } from "@testing-library/react";

const {
  createRoot,
  render,
  setApiBaseUrl,
  setTokenProvider,
  load,
  autoResolveIfNeeded,
  subscribe,
  restoreSession,
  probe,
  getAccessToken,
} = vi.hoisted(() => ({
  createRoot: vi.fn(),
  render: vi.fn(),
  setApiBaseUrl: vi.fn(),
  setTokenProvider: vi.fn(),
  load: vi.fn(),
  autoResolveIfNeeded: vi.fn(),
  subscribe: vi.fn(),
  restoreSession: vi.fn(),
  probe: vi.fn(),
  getAccessToken: vi.fn(),
}));

const state = {
  config: { apiBaseUrl: "https://api.example.com" },
  load,
  autoResolveIfNeeded,
};

vi.mock("react-dom/client", () => ({
  default: { createRoot },
  createRoot,
}));
vi.mock("./App", () => ({ default: () => null }));
vi.mock("./lib/apiClient", () => ({ setApiBaseUrl, setTokenProvider }));
vi.mock("./lib/config", () => ({
  useConfig: {
    getState: () => state,
    subscribe,
  },
}));
vi.mock("./lib/auth", () => ({
  getAccessToken,
  useAuth: { getState: () => ({ restoreSession }) },
}));
vi.mock("./lib/healthCheck", () => ({
  useHealthCheck: { getState: () => ({ probe }) },
}));

describe("application bootstrap", () => {
  beforeEach(() => {
    vi.resetModules();
    document.body.innerHTML = '<div id="root"></div>';
    for (const mock of [
      createRoot,
      render,
      setApiBaseUrl,
      setTokenProvider,
      load,
      autoResolveIfNeeded,
      subscribe,
      restoreSession,
      probe,
    ]) {
      mock.mockReset();
    }
    createRoot.mockReturnValue({ render });
    load.mockResolvedValue(undefined);
    autoResolveIfNeeded.mockResolvedValue(undefined);
    restoreSession.mockResolvedValue(true);
    probe.mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("wires clients, renders React, and completes startup work", async () => {
    await import("./main");

    expect(setTokenProvider).toHaveBeenCalledWith(getAccessToken);
    expect(createRoot).toHaveBeenCalledWith(document.getElementById("root"));
    expect(render).toHaveBeenCalledOnce();
    expect(subscribe).toHaveBeenCalledOnce();
    await waitFor(() => expect(autoResolveIfNeeded).toHaveBeenCalledOnce());
    expect(setApiBaseUrl).toHaveBeenCalledWith("https://api.example.com");
    expect(restoreSession).toHaveBeenCalledOnce();
    expect(probe).toHaveBeenCalledOnce();

    state.config.apiBaseUrl = "https://changed.example.com";
    const subscriber = subscribe.mock.calls[0][0];
    subscriber(state);
    expect(setApiBaseUrl).toHaveBeenLastCalledWith("https://changed.example.com");
  });

  it("renders global boot errors without using HTML injection", async () => {
    await import("./main");
    const root = document.getElementById("root")!;
    root.innerHTML = "<span>old</span>";

    window.onerror?.("Unexpected <script>", "app.js", 12, 34, new Error("boom"));

    expect(root.querySelector("h1")).toHaveTextContent("Boot Error");
    expect(root.querySelector("pre")).toHaveTextContent("Unexpected <script>");
    expect(root.querySelector("script")).toBeNull();
  });

  it("logs non-blocking session and health failures", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    restoreSession.mockRejectedValue(new Error("keychain unavailable"));
    probe.mockRejectedValue(new Error("health unavailable"));

    await import("./main");

    await waitFor(() => expect(warn).toHaveBeenCalledTimes(2));
    expect(autoResolveIfNeeded).toHaveBeenCalledOnce();
  });

  it("reports configuration boot failures", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    load.mockRejectedValue(new Error("config unavailable"));

    await import("./main");

    await waitFor(() => expect(error).toHaveBeenCalledWith(
      "Configuration boot failure:",
      expect.any(Error),
    ));
    expect(autoResolveIfNeeded).not.toHaveBeenCalled();
  });

  it("renders a safe failure when the root element is missing", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    document.body.innerHTML = "";

    await import("./main");

    expect(error).toHaveBeenCalledWith("React render failure:", expect.any(Error));
    expect(createRoot).not.toHaveBeenCalled();
  });
});
