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

function hasTimezoneOffset(iso: string): boolean {
  return /(?:Z|[+-]\d{2}:\d{2})$/i.test(iso.trim());
}

/** Parse API UTC timestamps into an instant for local display. */
export function parseUtcIso(value: string): Date {
  const trimmed = value.trim();
  if (!trimmed) return new Date(Number.NaN);
  if (hasTimezoneOffset(trimmed)) return new Date(trimmed);
  return new Date(`${trimmed}Z`);
}

/** Format a UTC ISO timestamp in the machine's local timezone. */
export function formatLocalDateTime(
  value: string,
  options?: Intl.DateTimeFormatOptions,
): string {
  const date = parseUtcIso(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString(undefined, {
    dateStyle: "short",
    timeStyle: "short",
    ...options,
  });
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
