import { create } from "zustand";
import { invoke } from "@tauri-apps/api/core";
import { useConfig } from "./config";

interface AuthState {
  accessToken: string | null;
  account: string | null;
  signedIn: boolean;
  setToken: (token: string | null, account?: string | null) => void;
  signOut: () => void;
}

export const useAuth = create<AuthState>((set) => ({
  accessToken: null,
  account: null,
  signedIn: false,
  setToken: (token, account = null) =>
    set({ accessToken: token, account, signedIn: !!token }),
  signOut: () => set({ accessToken: null, account: null, signedIn: false }),
}));

/** Token getter injected into the cloud API client. */
export async function getAccessToken(): Promise<string | null> {
  return useAuth.getState().accessToken;
}

/**
 * Entra auth-code + PKCE loopback flow.
 * Rust opens the browser, starts a localhost listener, catches the redirect,
 * exchanges the code, and returns the access token + account name.
 */
export async function signIn(): Promise<void> {
  const { authAuthority, authClientId, authScope } = useConfig.getState().config;

  if (!authAuthority || !authClientId) {
    throw new Error(
      "Auth authority and client ID must be configured in Settings before signing in."
    );
  }

  const result = await invoke<{ accessToken: string; account: string; expiresIn: number }>(
    "auth_sign_in",
    { authority: authAuthority, clientId: authClientId, scope: authScope }
  );

  useAuth.getState().setToken(result.accessToken, result.account);
}
