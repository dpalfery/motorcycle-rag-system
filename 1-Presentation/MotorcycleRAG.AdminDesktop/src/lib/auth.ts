import { create } from "zustand";
import { invoke } from "@tauri-apps/api/core";

// ── Types ────────────────────────────────────────────────────────────────────

/** Session shape returned by the Rust auth commands. */
export interface AuthSession {
  accessToken: string;
  account: string;
  /** Unix timestamp (seconds) when the access token expires. */
  expiresAt: number;
}

/** A Chrome user profile discovered on disk. */
export interface ChromeProfile {
  directory: string;
  name: string;
  userName?: string;
}

/**
 * Result of `auth_list_chrome_profiles` (D2).
 * On I/O or parse failure `profiles` is empty and `error` explains why —
 * never a silent sole fake Default.
 */
export interface ChromeProfilesResult {
  profiles: ChromeProfile[];
  /** Null on success; diagnostic string when discovery fails. */
  error: string | null;
}

/** Select value / persisted config marker for system-default-browser sign-in. */
export const SYSTEM_DEFAULT_BROWSER = "";

// ── Store ────────────────────────────────────────────────────────────────────

interface AuthState {
  accessToken: string | null;
  account: string | null;
  signedIn: boolean;
  /** Unix timestamp (seconds) when the current access token expires. */
  expiresAt: number | null;
  /** Prevents concurrent token-refresh calls. */
  isRefreshing: boolean;

  /** Replace the current session fields and mark signedIn = true. */
  setSession: (token: string, account: string, expiresAt: number) => void;
  /**
   * Full Entra PKCE sign-in via Rust.
   * Pass a Chrome profile directory, or `null`/`undefined` for the system default browser.
   */
  signIn: (chromeProfileDirectory?: string | null) => Promise<void>;
  /** Sign out: tells Rust to clear the keychain, then resets local state. */
  signOut: () => Promise<void>;
  /** Try to restore a previously-persisted session from the OS keychain. */
  restoreSession: () => Promise<boolean>;
  /** Refresh the access token using the stored refresh token. */
  refreshToken: () => Promise<boolean>;
}

export const useAuth = create<AuthState>((set, get) => ({
  accessToken: null,
  account: null,
  signedIn: false,
  expiresAt: null,
  isRefreshing: false,

  setSession: (token, account, expiresAt) =>
    set({
      accessToken: token,
      account,
      expiresAt,
      signedIn: true,
    }),

  signIn: async (chromeProfileDirectory?: string | null) => {
    const session = await invoke<AuthSession>("auth_sign_in", {
      profileDirectory: chromeProfileDirectory ?? null,
    });
    get().setSession(session.accessToken, session.account, session.expiresAt);
  },

  signOut: async () => {
    try {
      await invoke<void>("auth_sign_out");
    } finally {
      // Local state must not remain authenticated when the OS keychain is unavailable.
      set({
        accessToken: null,
        account: null,
        signedIn: false,
        expiresAt: null,
      });
    }
  },

  restoreSession: async () => {
    const session = await invoke<AuthSession | null>("auth_restore_session");
    if (session) {
      get().setSession(session.accessToken, session.account, session.expiresAt);
      return true;
    }
    return false;
  },

  refreshToken: async () => {
    if (get().isRefreshing) return false;
    set({ isRefreshing: true });
    try {
      const session = await invoke<AuthSession>("auth_refresh_token");
      get().setSession(session.accessToken, session.account, session.expiresAt);
      set({ isRefreshing: false });
      return true;
    } catch {
      set({ isRefreshing: false });
      return false;
    }
  },
}));

// ── Standalone helpers ───────────────────────────────────────────────────────

/** Token getter injected into the cloud API client (apiClient.ts). */
export async function getAccessToken(): Promise<string | null> {
  return useAuth.getState().accessToken;
}

/** List Chrome user profiles available on this machine (`{ profiles, error }`). */
export async function listChromeProfiles(): Promise<ChromeProfilesResult> {
  return invoke<ChromeProfilesResult>("auth_list_chrome_profiles");
}

/**
 * Convenience wrapper — delegates to the store's signIn method.
 * Existing components (e.g. SignInScreen) can import this directly.
 * Pass `null`/`undefined` to open the system default browser.
 */
export async function signIn(chromeProfileDirectory?: string | null): Promise<void> {
  return useAuth.getState().signIn(chromeProfileDirectory);
}
