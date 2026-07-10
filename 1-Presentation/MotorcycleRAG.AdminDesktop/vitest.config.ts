import { defineConfig } from "vitest/config";
import path from "path";

export default defineConfig({
  resolve: { alias: { "@": path.resolve(__dirname, "./src") } },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
  },
  coverage: {
    provider: "v8",
    reporter: ["text", "cobertura"],
    reportsDirectory: "./coverage",
    exclude: [
      "src/**/*.test.ts",
      "src/**/*.test.tsx",
      "src/test/**",
      "src/**/*.d.ts",
      "src-tauri/**",
      "dist/**",
    ],
  },
});
