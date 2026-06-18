import axios, { type AxiosInstance } from "axios";
import { trimTrailingSlash } from "./utils";

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

function addInterceptors(client: AxiosInstance) {
  client.interceptors.request.use(async (cfg) => {
    if (baseUrl) cfg.baseURL = baseUrl;
    const token = await tokenProvider();
    if (token) cfg.headers.Authorization = `Bearer ${token}`;
    return cfg;
  });
}

addInterceptors(instance);
addInterceptors(uploadApi);

export const api = instance;
