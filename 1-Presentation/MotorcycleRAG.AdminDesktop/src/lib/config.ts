import { create } from "zustand";
import { load, type Store } from "@tauri-apps/plugin-store";
import { discoverProcessorWorkingDir } from "./processor";

/**
 * Persisted operator configuration. Replaces the MAUI ConfigurationStateService /
 * SettingsService (Preferences). The upload-job secret is handled alongside the
 * other settings so operators can manage it from the Settings screen.
 */
export interface AppConfig {
  apiBaseUrl: string;
  authAuthority: string;
  authClientId: string;
  authScope: string;
  embeddingProviderEndpoint: string;
  embeddingModel: string;
  tokenizerModelPath: string;
  /** OpenAI-compatible endpoint for graph entity extraction (defaults to same as embedding). */
  graphExtractionEndpoint: string;
  /** Chat model used to extract graph entities and relationships. */
  graphExtractionModel: string;
  localProcessorPort: number;
  localProcessorWorkingDir: string;
  /** Azure Blob Storage account URL used by the local processor to read uploads. */
  azureStorageAccountUrl: string;
  /** If true, the app will try to auto-resolve the processor path on startup if empty. */
  autoResolveProcessor: boolean;
  /** Client secret for the Python-Upload-Job Entra app registration. */
  pythonUploadJobSecret: string;
}

export const DEFAULT_CONFIG: AppConfig = {
  apiBaseUrl: "https://motorag.api.palfery.com",
  authAuthority: "https://login.microsoftonline.com/0f8f8a52-f135-43af-af88-e0b54ca9ff91",
  authClientId: "a86e8458-4482-4bb6-808a-28d65b2668ef",
  authScope: "api://motorcyclerag-api/admin",
  embeddingProviderEndpoint: "http://localhost:1234/v1",
  embeddingModel: "qwen3-embedding",
  tokenizerModelPath: "",
  graphExtractionEndpoint: "http://localhost:1234/v1",
  graphExtractionModel: "microsoft/phi-4-reasoning-plus",
  localProcessorPort: 8100,
  localProcessorWorkingDir: "",
  azureStorageAccountUrl: "https://mcrragdevst0125c2ea3c.blob.core.windows.net/",
  autoResolveProcessor: true,
  pythonUploadJobSecret: "",
};

const STORE_FILE = "config.json";
const CONFIG_KEY = "appConfig";

interface ConfigState {
  config: AppConfig;
  loaded: boolean;
  load: () => Promise<void>;
  save: (patch: Partial<AppConfig>) => Promise<void>;
  /** Auto-resolves processor path only if it's currently empty and enabled. */
  autoResolveIfNeeded: () => Promise<void>;
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
  autoResolveIfNeeded: async () => {
    const { config } = get();
    if (config.autoResolveProcessor && !config.localProcessorWorkingDir) {
      const resolved = await discoverProcessorWorkingDir();
      if (resolved) {
        console.log("Auto-resolved local processor path:", resolved);
        set((state) => ({
          config: { ...state.config, localProcessorWorkingDir: resolved },
        }));
      }
    }
  },
}));
