import { useState, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { LogIn } from "lucide-react";
import { signIn, type ChromeProfile } from "@/lib/auth";
import { useConfig } from "@/lib/config";
import { Button } from "@/components/ui";

export default function SignInScreen() {
  const config = useConfig((s) => s.config);
  const saveConfig = useConfig((s) => s.save);

  const [profiles, setProfiles] = useState<ChromeProfile[]>([]);
  const [selectedProfile, setSelectedProfile] = useState<string>(config.selectedChromeProfile);
  const [loadingProfiles, setLoadingProfiles] = useState(true);

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Fetch Chrome profiles on mount
  useEffect(() => {
    let cancelled = false;

    async function loadProfiles() {
      try {
        const result = await invoke<ChromeProfile[]>("auth_list_chrome_profiles");
        if (cancelled) return;
        setProfiles(result);

        // Initialize selection: prefer persisted config value if it matches a real profile
        const persisted = useConfig.getState().config.selectedChromeProfile;
        const match = result.find((p) => p.directory === persisted);
        setSelectedProfile(match ? match.directory : "Default");
      } catch (e) {
        if (!cancelled) {
          console.warn("Failed to load Chrome profiles:", e);
        }
      } finally {
        if (!cancelled) {
          setLoadingProfiles(false);
        }
      }
    }

    void loadProfiles();
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleSignIn() {
    setBusy(true);
    setError(null);
    try {
      await signIn(selectedProfile);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Sign-in failed.");
    } finally {
      setBusy(false);
    }
  }

  function handleProfileChange(directory: string) {
    setSelectedProfile(directory);
    void saveConfig({ selectedChromeProfile: directory });
  }

  return (
    <div className="flex h-screen w-screen items-center justify-center bg-background">
      <div className="flex w-full max-w-sm flex-col items-center gap-6 rounded-2xl border border-border bg-card p-10 shadow-xl">
        <div className="flex h-14 w-14 items-center justify-center rounded-xl bg-primary/10">
          <LogIn className="h-7 w-7 text-primary" />
        </div>

        <div className="text-center">
          <h1 className="text-xl font-semibold tracking-tight">MotorcycleRAG Admin</h1>
          <p className="mt-1 text-sm text-muted">Sign in with your Microsoft account to continue.</p>
        </div>

        {/* Chrome profile selector */}
        <div className="w-full">
          <label
            htmlFor="chrome-profile-select"
            className="mb-1 block text-sm text-muted"
          >
            Chrome Profile
          </label>
          {loadingProfiles ? (
            <div className="text-sm text-muted">Loading profiles…</div>
          ) : profiles.length === 0 ? (
            <div className="text-sm text-muted">
              No Chrome profiles detected. Using Default.
            </div>
          ) : (
            <select
              id="chrome-profile-select"
              className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
              value={selectedProfile}
              onChange={(e) => handleProfileChange(e.target.value)}
            >
              {profiles.map((p) => (
                <option key={p.directory} value={p.directory}>
                  {p.name}
                  {p.userName ? ` (${p.userName})` : ""}
                </option>
              ))}
            </select>
          )}
        </div>

        {error && (
          <div className="w-full rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {error}
          </div>
        )}

        <Button
          variant="primary"
          className="w-full"
          onClick={() => void handleSignIn()}
          disabled={busy}
        >
          {busy ? "Signing in…" : "Sign in with Microsoft"}
        </Button>
      </div>
    </div>
  );
}
