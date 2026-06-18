import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import { setApiBaseUrl, setTokenProvider } from "./lib/apiClient";
import { useConfig } from "./lib/config";
import { getAccessToken } from "./lib/auth";
import "./index.css";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});

// Wire the cloud API client to current config + the auth token provider.
setTokenProvider(getAccessToken);
void useConfig
  .getState()
  .load()
  .then(() => setApiBaseUrl(useConfig.getState().config.apiBaseUrl));
useConfig.subscribe((s) => setApiBaseUrl(s.config.apiBaseUrl));

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);
