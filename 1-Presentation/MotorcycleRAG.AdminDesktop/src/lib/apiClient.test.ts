import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { invoke } from "@tauri-apps/api/core";
import { toast } from "sonner";
import { useAuth } from "@/lib/auth";
import { type AuthSession } from "@/lib/auth";
import { api, setTokenProvider, setApiBaseUrl } from "./apiClient";

// ── Mocks ────────────────────────────────────────────────────────────────────

vi.mock("@tauri-apps/api/core", () => ({
  invoke: vi.fn(),
}));

vi.mock("sonner", () => ({
  toast: { error: vi.fn() },
}));

// ── Helpers ──────────────────────────────────────────────────────────────────

/**
 * A mock axios adapter that returns responses with the given HTTP status codes
 * in order. Every call consumes one status code; extra calls default to 200.
 */
function mockAdapter(...statusCodes: number[]) {
  let index = 0;
  return (config: Record<string, unknown>) => {
    const status = index < statusCodes.length ? statusCodes[index++]! : 200;
    if (status >= 400) {
      return Promise.reject({
        response: { status, data: "Server Error" },
        config,
        isAxiosError: true,
      });
    }
    return Promise.resolve({
      data: { ok: true, url: config.url },
      status,
      statusText: "OK",
      headers: {},
      config,
    });
  };
}

// ── Setup / Teardown ─────────────────────────────────────────────────────────

// Save the default adapter so we can restore it after each test.
let defaultAdapter: typeof api.defaults.adapter;

describe("apiClient", () => {
  beforeAll(() => {
    defaultAdapter = api.defaults.adapter;
  });

  beforeEach(() => {
    vi.clearAllMocks();

    // Reset the auth store to signed-out state.
    useAuth.setState({
      accessToken: null,
      account: null,
      signedIn: false,
      expiresAt: null,
      isRefreshing: false,
    });

    // Reset module-level token / base-url configuration.
    setTokenProvider(async () => null);
    setApiBaseUrl("");

    // Restore the default HTTP adapter so each test starts clean.
    api.defaults.adapter = defaultAdapter;
  });

  afterEach(() => {
    api.defaults.adapter = defaultAdapter;
  });

  // ── 401 → refresh → retry ──────────────────────────────────────────────

  it("triggers token refresh and retries the request on 401", async () => {
    const newSession = {
      accessToken: "refreshed-token",
      account: "user@example.com",
      expiresAt: 9999,
    };
    vi.mocked(invoke).mockImplementation(async (cmd: string) => {
      if (cmd === "auth_refresh_token") return newSession as AuthSession;
      return undefined;
    });

    // Token provider returns an initial (expired) token.
    setTokenProvider(async () => "old-token");
    setApiBaseUrl("https://api.example.com");

    // Adapter: first call → 401, second call (the retry) → 200.
    api.defaults.adapter = mockAdapter(401, 200) as never;

    const response = await api.get("/test-endpoint");

    // ── Assertions ──────────────────────────────────────────────────────

    // The retry should succeed.
    expect(response.data).toEqual({ ok: true, url: "/test-endpoint" });

    // Exactly one refresh invocation.
    expect(invoke).toHaveBeenCalledWith("auth_refresh_token");
    expect(invoke).toHaveBeenCalledTimes(1);

    // The store should hold the refreshed session.
    expect(useAuth.getState().accessToken).toBe("refreshed-token");
    expect(useAuth.getState().signedIn).toBe(true);
  });

  it("does not retry the same request more than once on 401", async () => {
    vi.mocked(invoke).mockImplementation(async (cmd: string) => {
      if (cmd === "auth_refresh_token") {
        return {
          accessToken: "refreshed-token",
          account: "user@example.com",
          expiresAt: 9999,
        } as AuthSession;
      }
      return undefined;
    });

    setTokenProvider(async () => "old-token");
    setApiBaseUrl("https://api.example.com");

    // Both the original and retry return 401 — should NOT loop.
    api.defaults.adapter = mockAdapter(401, 401) as never;

    await expect(api.get("/test-endpoint")).rejects.toBeDefined();

    // Refresh was attempted once, then the retry also failed.
    expect(invoke).toHaveBeenCalledWith("auth_refresh_token");
    expect(invoke).toHaveBeenCalledTimes(1);

    // signOut should NOT be called here because the second 401 hits
    // _retry=true guard, which passes straight to Promise.reject.
  });

  // ── Single-flight refresh ─────────────────────────────────────────────

  it("shares a single refresh across concurrent 401 requests", async () => {
    const newSession = {
      accessToken: "refreshed-token",
      account: "user@example.com",
      expiresAt: 9999,
    };
    vi.mocked(invoke).mockImplementation(async (cmd: string) => {
      if (cmd === "auth_refresh_token") return newSession as AuthSession;
      return undefined;
    });

    setTokenProvider(async () => "old-token");
    setApiBaseUrl("https://api.example.com");

    // Adapter sequence: two 401s (original), then two 200s (retries).
    api.defaults.adapter = mockAdapter(401, 401, 200, 200) as never;

    const [r1, r2] = await Promise.all([
      api.get("/endpoint-1"),
      api.get("/endpoint-2"),
    ]);

    expect(r1.data).toEqual({ ok: true, url: "/endpoint-1" });
    expect(r2.data).toEqual({ ok: true, url: "/endpoint-2" });

    // Only ONE call to auth_refresh_token despite two 401s.
    expect(invoke).toHaveBeenCalledWith("auth_refresh_token");
    expect(invoke).toHaveBeenCalledTimes(1);
  });

  // ── Refresh failure → sign out ────────────────────────────────────────

  it("signs out and shows a toast when refresh fails", async () => {
    vi.mocked(invoke).mockImplementation(async (cmd: string) => {
      if (cmd === "auth_refresh_token")
        throw new Error("refresh failed");
      // auth_sign_out resolves normally.
      if (cmd === "auth_sign_out") return undefined;
      return undefined;
    });

    // Prime the store as signed-in so signOut actually transitions.
    useAuth.getState().setSession("stale-token", "user@example.com", 1);

    setTokenProvider(async () => "stale-token");
    setApiBaseUrl("https://api.example.com");

    api.defaults.adapter = mockAdapter(401) as never;

    await expect(api.get("/test-endpoint")).rejects.toBeDefined();

    // signOut was called.
    expect(invoke).toHaveBeenCalledWith("auth_sign_out");
    expect(useAuth.getState().signedIn).toBe(false);
    expect(useAuth.getState().accessToken).toBeNull();

    // Toast error was displayed.
    expect(toast.error).toHaveBeenCalledWith(
      "Your session has expired. Please sign in again.",
    );
  });

  // ── Token injection ────────────────────────────────────────────────────

  it("attaches the bearer token from the tokenProvider on every request", async () => {
    setTokenProvider(async () => "my-bearer-token");
    setApiBaseUrl("https://api.example.com");

    api.defaults.adapter = mockAdapter(200) as never;

    const response = await api.get("/secure-endpoint");

    expect(response.data).toEqual({ ok: true, url: "/secure-endpoint" });
  });

  it("sends requests without an Authorization header when token is null", async () => {
    setTokenProvider(async () => null);
    setApiBaseUrl("https://api.example.com");

    api.defaults.adapter = mockAdapter(200) as never;

    const response = await api.get("/public-endpoint");

    expect(response.data).toEqual({ ok: true, url: "/public-endpoint" });
  });
});
