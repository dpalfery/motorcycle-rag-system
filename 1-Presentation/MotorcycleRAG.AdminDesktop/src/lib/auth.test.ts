import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  useAuth,
  getAccessToken,
  signIn,
  listChromeProfiles,
  type AuthSession,
} from "./auth";

// ── Mock Tauri invoke ────────────────────────────────────────────────────────

vi.mock("@tauri-apps/api/core", () => ({
  invoke: vi.fn(),
}));

import { invoke } from "@tauri-apps/api/core";

// ── Helpers ──────────────────────────────────────────────────────────────────

function mockAuthSession(overrides: Partial<AuthSession> = {}): AuthSession {
  return {
    accessToken: "test-token-abc",
    account: "tester@example.com",
    expiresAt: 9999999999,
    ...overrides,
  };
}

// ── Tests ────────────────────────────────────────────────────────────────────

describe("auth store", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuth.setState({
      accessToken: null,
      account: null,
      signedIn: false,
      expiresAt: null,
      isRefreshing: false,
    });
  });

  describe("initial state", () => {
    it("starts with null token and not signed in", () => {
      expect(useAuth.getState().accessToken).toBeNull();
      expect(useAuth.getState().signedIn).toBe(false);
      expect(useAuth.getState().account).toBeNull();
      expect(useAuth.getState().expiresAt).toBeNull();
      expect(useAuth.getState().isRefreshing).toBe(false);
    });
  });

  describe("setSession", () => {
    it("sets token, account, expiry and marks signedIn", () => {
      useAuth.getState().setSession("token123", "user@example.com", 9999999999);

      const state = useAuth.getState();
      expect(state.accessToken).toBe("token123");
      expect(state.account).toBe("user@example.com");
      expect(state.signedIn).toBe(true);
      expect(state.expiresAt).toBe(9999999999);
    });

    it("updates existing session to a new session", () => {
      useAuth.getState().setSession("first", "old@example.com", 1000);
      useAuth.getState().setSession("second", "new@example.com", 2000);

      const state = useAuth.getState();
      expect(state.accessToken).toBe("second");
      expect(state.account).toBe("new@example.com");
      expect(state.expiresAt).toBe(2000);
      expect(state.signedIn).toBe(true);
    });
  });

  describe("signOut", () => {
    it("clears all auth state on signOut", async () => {
      useAuth.getState().setSession("token", "user", 9999);
      vi.mocked(invoke).mockResolvedValue(undefined);

      await useAuth.getState().signOut();

      expect(useAuth.getState().accessToken).toBeNull();
      expect(useAuth.getState().signedIn).toBe(false);
      expect(useAuth.getState().expiresAt).toBeNull();
      expect(useAuth.getState().account).toBeNull();
      expect(invoke).toHaveBeenCalledWith("auth_sign_out");
    });

    it("calls the Rust auth_sign_out command", async () => {
      vi.mocked(invoke).mockResolvedValue(undefined);

      await useAuth.getState().signOut();

      expect(invoke).toHaveBeenCalledTimes(1);
      expect(invoke).toHaveBeenCalledWith("auth_sign_out");
    });

    it("clears state even when already signed out", async () => {
      vi.mocked(invoke).mockResolvedValue(undefined);

      await useAuth.getState().signOut();

      expect(useAuth.getState().accessToken).toBeNull();
      expect(useAuth.getState().signedIn).toBe(false);
    });

    it("clears local state when keychain sign-out fails", async () => {
      useAuth.getState().setSession("token", "user", 9999);
      vi.mocked(invoke).mockRejectedValue(new Error("keychain unavailable"));

      await expect(useAuth.getState().signOut()).rejects.toThrow("keychain unavailable");

      expect(useAuth.getState().accessToken).toBeNull();
      expect(useAuth.getState().signedIn).toBe(false);
      expect(useAuth.getState().expiresAt).toBeNull();
      expect(useAuth.getState().account).toBeNull();
    });
  });

  describe("signIn", () => {
    it("calls auth_sign_in and sets session on success", async () => {
      const session = mockAuthSession();
      vi.mocked(invoke).mockResolvedValue(session);

      await useAuth.getState().signIn();

      expect(invoke).toHaveBeenCalledWith("auth_sign_in", {
        profileDirectory: null,
      });
      expect(useAuth.getState().accessToken).toBe(session.accessToken);
      expect(useAuth.getState().account).toBe(session.account);
      expect(useAuth.getState().signedIn).toBe(true);
    });

    it("passes optional profile directory to auth_sign_in", async () => {
      vi.mocked(invoke).mockResolvedValue(mockAuthSession());

      await useAuth.getState().signIn("Profile 1");

      expect(invoke).toHaveBeenCalledWith("auth_sign_in", {
        profileDirectory: "Profile 1",
      });
    });

    it("treats empty string as system default (null profileDirectory)", async () => {
      vi.mocked(invoke).mockResolvedValue(mockAuthSession());

      await useAuth.getState().signIn("");

      expect(invoke).toHaveBeenCalledWith("auth_sign_in", {
        profileDirectory: null,
      });
    });
  });

  describe("restoreSession", () => {
    it("returns true and sets session when restore succeeds", async () => {
      const session = mockAuthSession({ accessToken: "restored-token" });
      vi.mocked(invoke).mockResolvedValue(session);

      const result = await useAuth.getState().restoreSession();

      expect(result).toBe(true);
      expect(useAuth.getState().accessToken).toBe("restored-token");
      expect(useAuth.getState().signedIn).toBe(true);
    });

    it("returns false and does NOT set state when no session exists", async () => {
      vi.mocked(invoke).mockResolvedValue(null);

      const result = await useAuth.getState().restoreSession();

      expect(result).toBe(false);
      expect(useAuth.getState().accessToken).toBeNull();
      expect(useAuth.getState().signedIn).toBe(false);
    });
  });

  describe("refreshToken", () => {
    it("updates the session on successful refresh", async () => {
      const newSession = mockAuthSession({ accessToken: "refreshed-xyz" });
      vi.mocked(invoke).mockResolvedValue(newSession);

      const result = await useAuth.getState().refreshToken();

      expect(result).toBe(true);
      expect(invoke).toHaveBeenCalledWith("auth_refresh_token");
      expect(useAuth.getState().accessToken).toBe("refreshed-xyz");
      expect(useAuth.getState().signedIn).toBe(true);
      expect(useAuth.getState().isRefreshing).toBe(false);
    });

    it("returns false and stays signed out on refresh failure", async () => {
      vi.mocked(invoke).mockRejectedValue(new Error("refresh failed"));

      const result = await useAuth.getState().refreshToken();

      expect(result).toBe(false);
      expect(useAuth.getState().accessToken).toBeNull();
      expect(useAuth.getState().signedIn).toBe(false);
      expect(useAuth.getState().isRefreshing).toBe(false);
    });

    it("returns false without calling invoke when already refreshing", async () => {
      useAuth.setState({ isRefreshing: true });

      const result = await useAuth.getState().refreshToken();

      expect(result).toBe(false);
      expect(invoke).not.toHaveBeenCalled();
    });
  });
});

// ── Standalone helpers ───────────────────────────────────────────────────────

describe("getAccessToken", () => {
  it("returns the current access token when session exists", async () => {
    useAuth.getState().setSession("abc-token", "user", 9999);

    const token = await getAccessToken();
    expect(token).toBe("abc-token");
  });

  it("returns null when no session exists", async () => {
    useAuth.setState({ accessToken: null, signedIn: false });

    const token = await getAccessToken();
    expect(token).toBeNull();
  });
});

describe("signIn (standalone)", () => {
  it("delegates to the store signIn", async () => {
    const session = mockAuthSession();
    vi.mocked(invoke).mockResolvedValue(session);

    await signIn();

    const state = useAuth.getState();
    expect(state.accessToken).toBe(session.accessToken);
    expect(state.signedIn).toBe(true);
  });
});

describe("listChromeProfiles", () => {
  it("calls auth_list_chrome_profiles and returns { profiles, error }", async () => {
    const payload = {
      profiles: [
        { directory: "/profiles/1", name: "Default", userName: "alice" },
      ],
      error: null,
    };
    vi.mocked(invoke).mockResolvedValue(payload);

    const result = await listChromeProfiles();

    expect(result).toEqual(payload);
    expect(invoke).toHaveBeenCalledWith("auth_list_chrome_profiles");
  });

  it("surfaces discovery error from the Rust result shape", async () => {
    const payload = {
      profiles: [],
      error: "Could not read Chrome Local State",
    };
    vi.mocked(invoke).mockResolvedValue(payload);

    const result = await listChromeProfiles();

    expect(result.profiles).toEqual([]);
    expect(result.error).toBe("Could not read Chrome Local State");
  });
});
