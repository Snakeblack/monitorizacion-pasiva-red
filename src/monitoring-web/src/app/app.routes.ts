import { Routes } from '@angular/router';
import { SessionDetailPage } from './session-detail/session-detail-page.component';
import { SessionSearchPage } from './sessions/session-search-page.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'sessions' },
  { path: 'sessions', component: SessionSearchPage },
  { path: 'sessions/:eventId', component: SessionDetailPage },
];
