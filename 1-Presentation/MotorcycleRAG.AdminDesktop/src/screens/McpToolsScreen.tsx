import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import { api } from "@/lib/apiClient";
import { Button, PageHeader, Empty } from "@/components/ui";
import { cn } from "@/lib/utils";

interface McpTool {
  id: string;
  name: string;
  description?: string;
  isEnabled: boolean;
}

export default function McpToolsScreen() {
  const qc = useQueryClient();

  const tools = useQuery({
    queryKey: ["mcp-tools"],
    queryFn: async () => {
      const res = await api.get<{ items?: McpTool[] } | McpTool[]>(
        "/api/admin/mcp-tools"
      );
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
  });

  const toggle = useMutation({
    mutationFn: ({ id, isEnabled }: { id: string; isEnabled: boolean }) =>
      api.put(`/api/admin/mcp-tools/${id}`, { isEnabled }),
    onMutate: async ({ id, isEnabled }) => {
      await qc.cancelQueries({ queryKey: ["mcp-tools"] });
      const prev = qc.getQueryData<McpTool[]>(["mcp-tools"]);
      qc.setQueryData<McpTool[]>(["mcp-tools"], (old) =>
        old?.map((t) => (t.id === id ? { ...t, isEnabled } : t))
      );
      return { prev };
    },
    onError: (_e, _v, ctx) => {
      if (ctx?.prev) qc.setQueryData(["mcp-tools"], ctx.prev);
    },
    onSettled: () => qc.invalidateQueries({ queryKey: ["mcp-tools"] }),
  });

  const toolList = tools.data ?? [];
  const enabledCount = toolList.filter((t) => t.isEnabled).length;

  return (
    <div>
      <PageHeader
        title="MCP tools"
        subtitle="Enable or disable orchestrator tools"
        actions={
          <Button onClick={() => qc.invalidateQueries({ queryKey: ["mcp-tools"] })}>
            <RefreshCw className="h-4 w-4" /> Refresh
          </Button>
        }
      />

      {toolList.length > 0 && (
        <div className="mb-4 text-sm text-muted">
          {enabledCount} of {toolList.length} enabled
        </div>
      )}

      <div className="overflow-hidden rounded-xl border border-border">
        {tools.isLoading ? (
          <Empty>Loading tools…</Empty>
        ) : tools.isError ? (
          <Empty>Could not load MCP tools.</Empty>
        ) : toolList.length === 0 ? (
          <Empty>No MCP tools registered.</Empty>
        ) : (
          toolList.map((t) => (
            <div
              key={t.id}
              className="flex items-center justify-between border-b border-border px-4 py-3.5 last:border-b-0"
            >
              <div className="min-w-0 flex-1 pr-4">
                <div className="flex items-center gap-2">
                  <span className={cn("text-sm font-medium", !t.isEnabled && "text-muted")}>
                    {t.name}
                  </span>
                  {!t.isEnabled && (
                    <span className="rounded-full bg-secondary px-2 py-0.5 text-xs text-muted">
                      disabled
                    </span>
                  )}
                </div>
                {t.description && (
                  <div className="mt-0.5 truncate text-xs text-muted">{t.description}</div>
                )}
              </div>

              {/* Toggle switch */}
              <button
                role="switch"
                aria-checked={t.isEnabled}
                disabled={toggle.isPending}
                onClick={() => toggle.mutate({ id: t.id, isEnabled: !t.isEnabled })}
                className={cn(
                  "relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors focus-visible:outline-none disabled:opacity-50",
                  t.isEnabled ? "bg-primary" : "bg-secondary"
                )}
              >
                <span
                  className={cn(
                    "pointer-events-none inline-block h-4 w-4 translate-x-0 rounded-full bg-white shadow transition-transform",
                    t.isEnabled ? "translate-x-4" : "translate-x-0"
                  )}
                />
              </button>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
