import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const packageJsonPath = join(dirname(fileURLToPath(import.meta.url)), "..", "package.json");
const pkg = JSON.parse(readFileSync(packageJsonPath, "utf8")) as {
  dependencies?: Record<string, string>;
};

function parseMinimumVersion(range: string): { major: number; minor: number; patch: number } {
  const cleaned = range.replace(/^[\^~>=<]+/, "").split(" ")[0]!.split("||")[0]!.trim();
  const [major, minor = "0", patch = "0"] = cleaned.split(".");
  return { major: Number(major), minor: Number(minor), patch: Number(patch) };
}

function satisfiesAtLeast(range: string | undefined, minimum: string): boolean {
  if (!range) {
    return false;
  }
  const actual = parseMinimumVersion(range);
  const min = parseMinimumVersion(minimum);
  if (actual.major !== min.major) {
    return actual.major > min.major;
  }
  if (actual.minor !== min.minor) {
    return actual.minor > min.minor;
  }
  return actual.patch >= min.patch;
}

describe("package.json migration contract (React Router 8)", () => {
  it("declares react-router satisfying >=8.3.0", () => {
    expect(pkg.dependencies?.["react-router"]).toBeDefined();
    expect(satisfiesAtLeast(pkg.dependencies?.["react-router"], "8.3.0")).toBe(true);
  });

  it("does not declare react-router-dom", () => {
    expect(pkg.dependencies?.["react-router-dom"]).toBeUndefined();
  });

  it("declares react satisfying >=19.2.7", () => {
    expect(satisfiesAtLeast(pkg.dependencies?.react, "19.2.7")).toBe(true);
  });

  it("declares react-dom satisfying >=19.2.7", () => {
    expect(satisfiesAtLeast(pkg.dependencies?.["react-dom"], "19.2.7")).toBe(true);
  });
});
