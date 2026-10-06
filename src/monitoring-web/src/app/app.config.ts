import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { loadAuthConfig } from './auth/auth-config-loader';
import { authInterceptor } from './auth/auth.interceptor';
import { AuthConfigStore } from './auth/auth.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideRouter(routes),
    // The authentication configuration is read before the first navigation so the guards know whether (and how) to sign in.
    provideAppInitializer(() => loadAuthConfig(inject(AuthConfigStore))),
  ],
};
