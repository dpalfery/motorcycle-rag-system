import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const packageJsonPath = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../package.json",
);

type PackageJson = {
  dependencies?: Record<string, string>;
  devDependencies?: Record<string, string>;
};

type Semver = {
  major: number;
  minor: number;
  patch: number;
};

function loadPackageJson(): PackageJson {
  return JSON.parse(readFileSync(packageJsonPath, "utf-8")) as PackageJson;
}

function getDeclaredVersion(
  pkg: PackageJson,
  name: string,
): string | undefined {
  return pkg.dependencies?.[name] ?? pkg.devDependencies?.[name];
}

function parseMinVersion(range: string): Semver | null {
  const match = range.match(/(\d+)\.(\d+)\.(\d+)/);
  if (!match) {
    return null;
  }

  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3]),
  };
}

function satisfiesAtLeast(range: string | undefined, minimum: Semver): boolean {
  if (!range) {
    return false;
  }

  const version = parseMinVersion(range);
  if (!version) {
    return false;
  }

  if (version.major !== minimum.major) {
    return version.major > minimum.major;
  }

  if (version.minor !== minimum.minor) {
    return version.minor > minimum.minor;
  }

  return version.patch >= minimum.patch;
}

describe("package.json migration contract (React Router 8 + React 19.2.7)", () => {
  const pkg = loadPackageJson();

  it("declares react-router satisfying >=8.3.0", () => {
    const range = getDeclaredVersion(pkg, "react-router");
    expect(range, "react-router must be a direct dependency").toBeDefined();
    expect(
      satisfiesAtLeast(range, { major: 8, minor: 3, patch: 0 }),
      `react-router ${range} must satisfy >=8.3.0`,
    ).toBe(true);
  });

  it("does not declare react-router-dom", () => {
    expect(getDeclaredVersion(pkg, "react-router-dom")).toBeUndefined();
  });

  it("declares react satisfying >=19.2.7", () => {
    const range = getDeclaredVersion(pkg, "react");
    expect(range).toBeDefined();
    expect(
      satisfiesAtLeast(range, { major: 19, minor: 2, patch: 7 }),
      `react ${range} must satisfy >=19.2.7`,
    ).toBe(true);
  });

  it("declares react-dom satisfying >=19.2.7", () => {
    const range = getDeclaredVersion(pkg, "react-dom");
    expect(range).toBeDefined();
    expect(
      satisfiesAtLeast(range, { major: 19, minor: 2, patch: 7 }),
      `react-dom ${range} must satisfy >=19.2.7`,
    ).toBe(true);
  });
});
