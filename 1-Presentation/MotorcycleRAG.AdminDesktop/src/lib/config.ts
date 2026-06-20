import { create } from "zustand";
import { load, type Store } from "@tauri-apps/plugin-store";

/**
 * Persisted operator configuration. Replaces the MAUI ConfigurationStateService /
 * SettingsService (Preferences). The upload-job secret is the one sensitive value and
 * is kept out of this store (handled separately via the OS keychain in the auth task).
 */
export interface AppConfig {
  apiBaseUrl: string;
  authAuthority: string;
  authClientId: string;
  authScope: string;
  embeddingProviderEndpoint: string;
  embeddingModel: string;
  localProcessorPort: number;
  localProcessorWorkingDir: string;
}

export const DEFAULT_CONFIG: AppConfig = {
  apiBaseUrl: "https://localhost:7215",
  authAuthority: "https://login.microsoftonline.com/0f8f8a52-f135-43af-af88-e0b54ca9ff91",
  authClientId: "a86e8458-4482-4bb6-808a-28d65b2668ef",
  authScope: "api://motorcyclerag-api/admin",
  embeddingProviderEndpoint: "http://localhost:11434",
  embeddingModel: "qwen3-embedding",
  localProcessorPort: 8100,
  localProcessorWorkingDir: "",
};

const STORE_FILE = "config.json";
const CONFIG_KEY = "appConfig";

interface ConfigState {
  config: AppConfig;
  loaded: boolean;
  load: () => Promise<void>;
  save: (patch: Partial<AppConfig>) => Promise<void>;
}

let storePromise: Promise<Store> | null = null;
function getStore(): Promise<Store> {
  if (!storePromise) {
    storePromise = load(STORE_FILE, { autoSave: true, defaults: {} }).catch((err) => {
      storePromise = null;
      throw err;
    });
  }
  return storePromise;
}

export const useConfig = create<ConfigState>((set, get) => ({
  config: DEFAULT_CONFIG,
  loaded: false,
  load: async () => {
    try {
      const store = await getStore();
      const saved = (await store.get<Partial<AppConfig>>(CONFIG_KEY)) ?? {};
      set({ config: { ...DEFAULT_CONFIG, ...saved }, loaded: true });
    } catch (e) {
      console.warn("Failed to load config from store, using defaults:", e);
      set({ config: DEFAULT_CONFIG, loaded: true });
    }
  },
  save: async (patch) => {
    const next = { ...get().config, ...patch };
    set({ config: next });
    const store = await getStore();
    await store.set(CONFIG_KEY, next);
    await store.save();
  },
}));
