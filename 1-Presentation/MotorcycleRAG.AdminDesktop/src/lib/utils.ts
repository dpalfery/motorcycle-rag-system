import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/** Merge Tailwind class names, de-duplicating conflicts. Ported from MotorcycleRag.WebUI. */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}

/** Normalize a base URL by trimming a single trailing slash. */
export function trimTrailingSlash(url: string): string {
  return url.replace(/\/+$/, "");
}

/**
 * Validate an http(s) URL, optionally allowing localhost/loopback hosts.
 * Mirrors the rules in MotorcycleRAG.Admin/Utilities/UrlValidator.
 */
export function isValidUrl(candidate: string, allowLocalhost = false): boolean {
  if (!candidate || !candidate.trim()) return false;
  let parsed: URL;
  try {
    parsed = new URL(candidate.trim());
  } catch {
    return false;
  }
  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return false;
  const host = parsed.hostname.toLowerCase();
  const isLoopback =
    host === "localhost" ||
    host === "127.0.0.1" ||
    host === "::1" ||
    host === "[::1]";
  if (isLoopback && !allowLocalhost) return false;
  return true;
}
