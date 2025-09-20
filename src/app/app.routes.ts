import { Routes } from '@angular/router';
import { SettingsComponent } from './components/settings/settings.component';
import { HomeComponent } from './components/home/home.component';

export const routes: Routes = [
  { path: 'home', component: HomeComponent },
  { path: 'settings', component: SettingsComponent },
  { path: '', redirectTo: '/home', pathMatch: 'full' }
];
