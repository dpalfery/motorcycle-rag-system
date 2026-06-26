import { useEffect, useState } from "react";
import { Check } from "lucide-react";
import { useConfig, type AppConfig } from "@/lib/config";
import { Button, Card, PageHeader } from "@/components/ui";
import { isValidUrl } from "@/lib/utils";

function Field({
  label,
  value,
  onChange,
  mono,
  placeholder,
  invalid,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  mono?: boolean;
  placeholder?: string;
  invalid?: boolean;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs text-muted">{label}</span>
      <input
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        className={`w-full rounded-md border bg-background/40 px-2.5 py-1.5 text-sm outline-none focus:border-primary ${
          invalid ? "border-danger" : "border-border"
        } ${mono ? "font-mono" : ""}`}
      />
    </label>
  );
}

export default function SettingsScreen() {
  const { config, save } = useConfig();
  const [draft, setDraft] = useState<AppConfig>(config);
  const [saved, setSaved] = useState(false);

  useEffect(() => setDraft(config), [config]);

  const set = (patch: Partial<AppConfig>) => {
    setDraft((d) => ({ ...d, ...patch }));
    setSaved(false);
  };

  const apiUrlInvalid = !!draft.apiBaseUrl && !isValidUrl(draft.apiBaseUrl, true);

  const onSave = async () => {
    await save(draft);
    setSaved(true);
  };

  return (
    <div className="max-w-3xl">
      <PageHeader
        title="Settings"
        subtitle="Endpoints, authentication and the local processor"
        actions={
          <Button variant="primary" onClick={onSave} disabled={apiUrlInvalid}>
            {saved ? <Check className="h-4 w-4" /> : null}
            {saved ? "Saved" : "Save"}
          </Button>
        }
      />

      <Card className="mb-4">
        <div className="mb-3 text-sm font-medium">Cloud API</div>
        <Field label="API base URL" value={draft.apiBaseUrl} mono invalid={apiUrlInvalid} onChange={(v) => set({ apiBaseUrl: v })} />
      </Card>

      <Card className="mb-4">
        <div className="mb-3 text-sm font-medium">Authentication (Microsoft Entra)</div>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field label="Authority" value={draft.authAuthority} mono placeholder="https://login.microsoftonline.com/{tenant}" onChange={(v) => set({ authAuthority: v })} />
          <Field label="Client ID" value={draft.authClientId} mono onChange={(v) => set({ authClientId: v })} />
          <Field label="Scope" value={draft.authScope} mono onChange={(v) => set({ authScope: v })} />
        </div>
      </Card>

      <Card className="mb-4">
        <div className="mb-3 text-sm font-medium">Embedding provider</div>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field label="Endpoint" value={draft.embeddingProviderEndpoint} mono onChange={(v) => set({ embeddingProviderEndpoint: v })} />
          <Field label="Model" value={draft.embeddingModel} onChange={(v) => set({ embeddingModel: v })} />
        </div>
      </Card>

      <Card>
        <div className="mb-3 flex items-center justify-between">
          <div className="text-sm font-medium">Local processor</div>
          <div className="flex items-center gap-2">
            <input
              type="checkbox"
              id="auto-resolve"
              checked={draft.autoResolveProcessor}
              onChange={(e) => set({ autoResolveProcessor: e.target.checked })}
              className="h-3.5 w-3.5 rounded border-border bg-background/40"
            />
            <label htmlFor="auto-resolve" className="text-xs text-muted">
              Auto-resolve location
            </label>
          </div>
        </div>
        <div className="mb-3 text-xs text-muted">
          The application resolves the processor location automatically in development and packaged runs. 
          Use the field below only to override the default discovery.
        </div>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field 
            label="Working directory override" 
            value={draft.localProcessorWorkingDir} 
            mono 
            placeholder={draft.autoResolveProcessor ? "Auto-resolving..." : "Paste directory path..."} 
            onChange={(v) => set({ localProcessorWorkingDir: v })} 
          />
          <Field label="Port" value={String(draft.localProcessorPort)} mono onChange={(v) => set({ localProcessorPort: Number(v) || 8100 })} />
        </div>
      </Card>
    </div>
  );
}
