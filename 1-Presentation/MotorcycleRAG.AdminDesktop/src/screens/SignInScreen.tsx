import { useState } from "react";
import { LogIn } from "lucide-react";
import { signIn } from "@/lib/auth";
import { Button } from "@/components/ui";

export default function SignInScreen() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSignIn() {
    setBusy(true);
    setError(null);
    try {
      await signIn();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Sign-in failed.");
    } finally {
      setBusy(false);
    }
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
