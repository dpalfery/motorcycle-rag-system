import { describe, it, expect, vi } from "vitest";
import { discoverProcessorWorkingDir } from "./processor";
import { invoke } from "@tauri-apps/api/core";

vi.mock("@tauri-apps/api/core", () => ({
  invoke: vi.fn(),
}));

describe("processor discovery", () => {
  it("calls the backend deterministic resolver", async () => {
    const expectedPath = "/repo/2-Application/local-processing-service";
    vi.mocked(invoke).mockResolvedValue(expectedPath);

    const result = await discoverProcessorWorkingDir();
    
    expect(invoke).toHaveBeenCalledWith("resolve_processor_path", { configuredOverride: "" });
    expect(result).toBe(expectedPath);
  });

  it("handles backend failures gracefully", async () => {
    vi.mocked(invoke).mockRejectedValue(new Error("Resolution failed"));

    const result = await discoverProcessorWorkingDir();
    expect(result).toBe("");
  });
});
