export function isPathSafe(path: string): boolean {
  if (path.includes('\0')) return false;

  // Check UNC paths BEFORE normalization (normalization collapses \\ to /)
  if (/^[\\\/]{2}/.test(path)) return false;

  let decoded = path;
  try {
    decoded = decodeURIComponent(path);
    const doubleDecoded = decodeURIComponent(decoded);
    if (doubleDecoded !== decoded) decoded = doubleDecoded;
  } catch {
    // If decode fails, use original
  }

  const normalized = decoded.replace(/\\/g, '/').replace(/\/+/g, '/');

  if (/(^|\/)\.\.($|\/)/.test(normalized)) return false;
  if (/^\/(?:etc|proc|sys|dev|boot|root)(?:\/|$)/.test(normalized)) return false;
  if (/^[A-Za-z]:[\\\/](?:Windows|System32|Program\s*Files)(?:[\\\/]|$)/i.test(normalized)) return false;

  return true;
}
