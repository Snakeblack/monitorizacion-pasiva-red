import { sessionErrorMessage } from './session-http-error';

describe('Session error messages', () => {
  it('asks the person to sign in when a provider is configured', () => {
    expect(sessionErrorMessage(401)).toContain('Inicia sesión');
    expect(sessionErrorMessage(401, true)).toContain('Inicia sesión');
  });

  it('explains the real cause, not a dead end, when the server wants a sign-in but this console has no identity provider', () => {
    const message = sessionErrorMessage(401, false);
    expect(message).toContain('sin proveedor de identidad');
    expect(message).toContain('start:lab');
  });

  it('leaves every other status as it was', () => {
    expect(sessionErrorMessage(403, false)).toBe(sessionErrorMessage(403, true));
    expect(sessionErrorMessage(429)).toContain('Demasiadas consultas');
  });
});
