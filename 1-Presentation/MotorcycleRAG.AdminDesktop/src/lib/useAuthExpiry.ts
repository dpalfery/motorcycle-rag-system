import { useEffect, useRef } from "react";
import { useAuth } from "@/lib/auth";
import { refreshAccessToken } from "@/lib/apiClient";
import { toast } from "sonner";

/** Refresh the access token 5 minutes before it expires. */
const REFRESH_BUFFER_MS = 5 * 60 * 1000;

export function useAuthExpiry() {
  const { expiresAt, signOut, signedIn } = useAuth();
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;

    // Clear any existing timer whenever deps change
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }

    if (!signedIn || !expiresAt) return;

    const expiresAtMs = expiresAt * 1000;
    const now = Date.now();
    const refreshAt = expiresAtMs - REFRESH_BUFFER_MS;
    const delay = refreshAt - now;

    const doRefresh = async () => {
      const newToken = await refreshAccessToken();
      if (!mountedRef.current || !useAuth.getState().signedIn) return;
      if (!newToken) {
        toast.error("Your session has expired. Please sign in again.");
        await signOut().catch(() => {});
      }
    };

    if (delay <= 0) {
      // Token is already within 5 min of expiry (or expired) — refresh immediately
      doRefresh();
      return;
    }

    timerRef.current = setTimeout(doRefresh, delay);

    return () => {
      mountedRef.current = false;
      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }
    };
  }, [expiresAt, signedIn, signOut]);
}
