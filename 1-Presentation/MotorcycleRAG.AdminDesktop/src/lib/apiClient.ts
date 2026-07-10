import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from "axios";
import { invoke } from "@tauri-apps/api/core";
import { toast } from "sonner";
import { trimTrailingSlash } from "./utils";
import { useAuth, type AuthSession } from "@/lib/auth";

/**
 * Centralized client for the cloud .NET API (MotorcycleRAG.API). The MAUI app's ApiClient
 * attached an Entra bearer token to every call; we do the same via an interceptor. The
 * token getter is injected by the auth layer so this module stays auth-implementation-agnostic.
 */

let tokenProvider: () => Promise<string | null> = async () => null;
export function setTokenProvider(fn: () => Promise<string | null>): void {
  tokenProvider = fn;
}

let baseUrl = "";
export function setApiBaseUrl(url: string): void {
  baseUrl = trimTrailingSlash(url);
}

const instance: AxiosInstance = axios.create({ timeout: 30_000 });

/** Use for file uploads — no timeout so large files aren't cut off. */
export const uploadApi: AxiosInstance = axios.create({ timeout: 0 });

// ── Single-flight token refresh ─────────────────────────────────────────────
//
// When multiple concurrent requests receive 401 simultaneously, we must NOT
// trigger multiple refresh calls. `refreshPromise` ensures all 401s during a
// refresh wait for the same in-flight operation.
//
// We call invoke("auth_refresh_token") directly rather than going through
// useAuth.getState().refreshToken() because the store's refreshToken() has its
// own isRefreshing guard that would return false immediately for concurrent
// callers, causing premature sign-out. The interceptor is the sole
// single-flight coordinator.

let refreshPromise: Promise<string | null> | null = null;

export async function refreshAccessToken(): Promise<string | null> {
  if (!refreshPromise) {
    refreshPromise = invoke<AuthSession>("auth_refresh_token")
      .then((session) => {
        useAuth
          .getState()
          .setSession(session.accessToken, session.account, session.expiresAt);
        return session.accessToken;
      })
      .catch(() => null)
      .finally(() => {
        refreshPromise = null;
      });
  }
  return refreshPromise;
}

// ── Interceptors ─────────────────────────────────────────────────────────────

function addInterceptors(client: AxiosInstance) {
  client.interceptors.request.use(async (cfg) => {
    if (baseUrl) cfg.baseURL = baseUrl;
    const token = await tokenProvider();
    if (token) cfg.headers.Authorization = `Bearer ${token}`;
    return cfg;
  });

  client.interceptors.response.use(
    (response) => response,
    async (error) => {
      const originalRequest = error.config as InternalAxiosRequestConfig & {
        _retry?: boolean;
      };

      if (error.response?.status === 401 && originalRequest && !originalRequest._retry) {
        originalRequest._retry = true;

        const newToken = await refreshAccessToken();
        if (newToken) {
          originalRequest.headers.Authorization = `Bearer ${newToken}`;
          return client(originalRequest);
        }

        // Refresh failed — sign out and notify the user.
        await useAuth.getState().signOut().catch(() => {});
        toast.error("Your session has expired. Please sign in again.");
        return Promise.reject(error);
      }

      return Promise.reject(error);
    },
  );
}

addInterceptors(instance);
addInterceptors(uploadApi);

export const api = instance;
