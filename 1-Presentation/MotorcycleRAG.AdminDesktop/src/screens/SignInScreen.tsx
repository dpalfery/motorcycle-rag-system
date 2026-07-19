import { useState, useEffect } from "react";
import { LogIn } from "lucide-react";
import {
  listChromeProfiles,
  signIn,
  SYSTEM_DEFAULT_BROWSER,
  type ChromeProfile,
} from "@/lib/auth";
import { useConfig } from "@/lib/config";
import { Button } from "@/components/ui";

function formatInvokeError(e: unknown): string {
  if (typeof e === "string") return e;
  if (e instanceof Error) return e.message;
  return "Sign-in failed.";
}

export default function SignInScreen() {
  const config = useConfig((s) => s.config);
  const saveConfig = useConfig((s) => s.save);

  const [profiles, setProfiles] = useState<ChromeProfile[]>([]);
  const [profilesError, setProfilesError] = useState<string | null>(null);
  const [selectedProfile, setSelectedProfile] = useState<string>(
    config.selectedChromeProfile,
  );
  const [loadingProfiles, setLoadingProfiles] = useState(true);

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const profilesUnavailable = !loadingProfiles && (profilesError !== null || profiles.length === 0);

  // Fetch Chrome profiles on mount
  useEffect(() => {
    let cancelled = false;

    async function loadProfiles() {
      try {
        const result = await listChromeProfiles();
        if (cancelled) return;

        setProfiles(result.profiles);
        setProfilesError(result.error);

        const persisted = useConfig.getState().config.selectedChromeProfile;
        if (result.error || result.profiles.length === 0) {
          setSelectedProfile(SYSTEM_DEFAULT_BROWSER);
          return;
        }

        const match = result.profiles.find((p) => p.directory === persisted);
        if (match) {
          setSelectedProfile(match.directory);
        } else {
          // Recommended when no persisted Chrome profile match.
          setSelectedProfile(SYSTEM_DEFAULT_BROWSER);
        }
      } catch (e) {
        if (!cancelled) {
          console.warn("Failed to load Chrome profiles:", e);
          setProfiles([]);
          setProfilesError(formatInvokeError(e));
          setSelectedProfile(SYSTEM_DEFAULT_BROWSER);
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
      const profileDirectory =
        selectedProfile === SYSTEM_DEFAULT_BROWSER ? null : selectedProfile;
      await signIn(profileDirectory);
    } catch (e) {
      setError(formatInvokeError(e));
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
            Browser for sign-in
          </label>
          {loadingProfiles ? (
            <div className="text-sm text-muted">Loading profiles…</div>
          ) : (
            <div className="space-y-2">
              {profilesUnavailable && (
                <div className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  {profilesError ?? "Could not read Chrome profiles"}
                </div>
              )}
              <select
                id="chrome-profile-select"
                className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                value={selectedProfile}
                onChange={(e) => handleProfileChange(e.target.value)}
              >
                <option value={SYSTEM_DEFAULT_BROWSER}>
                  System default browser (recommended)
                </option>
                {profiles.map((p) => (
                  <option key={p.directory} value={p.directory}>
                    {p.name}
                    {p.userName ? ` (${p.userName})` : ""}
                  </option>
                ))}
              </select>
            </div>
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
