import { Routes } from '@angular/router';
import { SessionDetailPage } from './session-detail/session-detail-page.component';

export const routes: Routes = [
  { path: 'sessions/:eventId', component: SessionDetailPage },
];
