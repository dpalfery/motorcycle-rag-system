import { describe, it, expect, vi, beforeEach } from "vitest";
import { useConfig, DEFAULT_CONFIG } from "./config";
import { load as loadStore } from "@tauri-apps/plugin-store";
import { discoverProcessorWorkingDir } from "./processor";

vi.mock("@tauri-apps/plugin-store", () => ({
  load: vi.fn(),
}));

vi.mock("./processor", () => ({
  discoverProcessorWorkingDir: vi.fn(),
}));

describe("config store", () => {
  const mockStore = {
    get: vi.fn(),
    set: vi.fn(),
    save: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(loadStore).mockResolvedValue(mockStore as any);
    // Reset Zustand store state to defaults before each test
    useConfig.setState({ config: DEFAULT_CONFIG, loaded: false });
  });

  it("has correct defaults for processor auto-resolution", () => {
    expect(DEFAULT_CONFIG.localProcessorWorkingDir).toBe("");
    expect(DEFAULT_CONFIG.autoResolveProcessor).toBe(true);
  });

  describe("load", () => {
    it("merges saved config with defaults (precedence)", async () => {
      const savedConfig = {
        apiBaseUrl: "https://custom-api.com",
        localProcessorPort: 9999,
      };
      mockStore.get.mockResolvedValue(savedConfig);

      await useConfig.getState().load();

      const state = useConfig.getState().config;
      expect(state.apiBaseUrl).toBe("https://custom-api.com");
      expect(state.localProcessorPort).toBe(9999);
      // Ensure other fields preserved from defaults
      expect(state.embeddingModel).toBe(DEFAULT_CONFIG.embeddingModel);
      expect(state.autoResolveProcessor).toBe(DEFAULT_CONFIG.autoResolveProcessor);
    });

    it("uses all defaults when store returns nothing", async () => {
      mockStore.get.mockResolvedValue(null);

      await useConfig.getState().load();

      expect(useConfig.getState().config).toEqual(DEFAULT_CONFIG);
    });

    it("handles missing values in saved config", async () => {
      // Specifically test a partial object that might missing some keys
      mockStore.get.mockResolvedValue({ authAuthority: "https://mytenant.com" });

      await useConfig.getState().load();

      const state = useConfig.getState().config;
      expect(state.authAuthority).toBe("https://mytenant.com");
      expect(state.authClientId).toBe(DEFAULT_CONFIG.authClientId);
    });
  });

  describe("autoResolveIfNeeded", () => {
    it("resolves and updates state when empty and enabled", async () => {
      useConfig.setState({
        config: { ...DEFAULT_CONFIG, localProcessorWorkingDir: "", autoResolveProcessor: true },
      });
      vi.mocked(discoverProcessorWorkingDir).mockResolvedValue("/resolved/path");

      await useConfig.getState().autoResolveIfNeeded();

      expect(useConfig.getState().config.localProcessorWorkingDir).toBe("/resolved/path");
      expect(discoverProcessorWorkingDir).toHaveBeenCalled();
    });

    it("does NOT resolve when working dir is already set", async () => {
      useConfig.setState({
        config: { ...DEFAULT_CONFIG, localProcessorWorkingDir: "/existing/path", autoResolveProcessor: true },
      });

      await useConfig.getState().autoResolveIfNeeded();

      expect(useConfig.getState().config.localProcessorWorkingDir).toBe("/existing/path");
      expect(discoverProcessorWorkingDir).not.toHaveBeenCalled();
    });

    it("does NOT resolve when auto-resolve is disabled", async () => {
      useConfig.setState({
        config: { ...DEFAULT_CONFIG, localProcessorWorkingDir: "", autoResolveProcessor: false },
      });

      await useConfig.getState().autoResolveIfNeeded();

      expect(useConfig.getState().config.localProcessorWorkingDir).toBe("");
      expect(discoverProcessorWorkingDir).not.toHaveBeenCalled();
    });

    it("does nothing if resolution fails", async () => {
      useConfig.setState({
        config: { ...DEFAULT_CONFIG, localProcessorWorkingDir: "", autoResolveProcessor: true },
      });
      vi.mocked(discoverProcessorWorkingDir).mockResolvedValue("");

      await useConfig.getState().autoResolveIfNeeded();

      expect(useConfig.getState().config.localProcessorWorkingDir).toBe("");
    });
  });
});
