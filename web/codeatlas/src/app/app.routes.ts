import { Routes } from '@angular/router';
import { Explorer } from './features/explorer/explorer';
import { Cycles } from './features/cycles/cycles';

export const routes: Routes = [
  { path: '', redirectTo: '/explorer', pathMatch: 'full' },
  { path: 'explorer', component: Explorer },
  { path: 'cycles', component: Cycles },
  { path: '**', redirectTo: '/explorer' }
];
