export type Claims = Record<string, unknown>;

export const KNOWN_ROLES = ['analista', 'auditor', 'administrador-inventario'] as const;
export type Role = (typeof KNOWN_ROLES)[number];

// Reads the payload of a token WITHOUT verifying it. It exists only to show who is signed in and to hide actions the user cannot
// perform; every authorization decision is made by the API from the token it validates.
export function decodeClaims(token: string): Claims | null {
  const parts = token.split('.');
  if (parts.length !== 3) return null;
  try {
    const base64 = parts[1].replaceAll('-', '+').replaceAll('_', '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const bytes = Uint8Array.from(atob(padded), (char) => char.charCodeAt(0));
    const value: unknown = JSON.parse(new TextDecoder().decode(bytes));
    return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Claims) : null;
  } catch {
    return null;
  }
}

export function rolesOf(claims: Claims | null): Role[] {
  const roles = claims?.['roles'];
  if (!Array.isArray(roles)) return [];
  return roles.filter((role): role is Role => typeof role === 'string' && (KNOWN_ROLES as readonly string[]).includes(role));
}
