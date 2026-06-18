import { describe, it, expect } from "vitest";
import { cn, isValidUrl, trimTrailingSlash } from "./utils";

describe("trimTrailingSlash", () => {
  it("removes trailing slashes", () => {
    expect(trimTrailingSlash("https://x/")).toBe("https://x");
    expect(trimTrailingSlash("https://x///")).toBe("https://x");
    expect(trimTrailingSlash("https://x")).toBe("https://x");
  });
});

describe("isValidUrl", () => {
  it("accepts https and http", () => {
    expect(isValidUrl("https://api.example.com")).toBe(true);
    expect(isValidUrl("http://api.example.com")).toBe(true);
  });

  it("rejects non-http schemes and garbage", () => {
    expect(isValidUrl("ftp://x")).toBe(false);
    expect(isValidUrl("not a url")).toBe(false);
    expect(isValidUrl("")).toBe(false);
  });

  it("gates localhost behind the allowLocalhost flag", () => {
    expect(isValidUrl("http://localhost:8100")).toBe(false);
    expect(isValidUrl("http://localhost:8100", true)).toBe(true);
    expect(isValidUrl("http://127.0.0.1:11434", true)).toBe(true);
  });
});

describe("cn", () => {
  it("merges and de-duplicates tailwind classes", () => {
    expect(cn("px-2", "px-4")).toBe("px-4");
    expect(cn("text-sm", false, "font-medium")).toBe("text-sm font-medium");
  });
});
