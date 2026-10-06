import { Routes } from '@angular/router';
import { AuthCallbackPage, SignedOutPage } from './auth/auth-pages';
import { authGuard } from './auth/auth.guard';
import { InventoryPage } from './inventory/inventory-page.component';
import { SessionDetailPage } from './session-detail/session-detail-page.component';
import { SessionSearchPage } from './sessions/session-search-page.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'sessions' },
  { path: 'auth/callback', component: AuthCallbackPage },
  { path: 'signed-out', component: SignedOutPage },
  { path: 'sessions', component: SessionSearchPage, canActivate: [authGuard] },
  { path: 'inventory', component: InventoryPage, canActivate: [authGuard] },
  { path: 'sessions/:eventId', component: SessionDetailPage, canActivate: [authGuard] },
];
