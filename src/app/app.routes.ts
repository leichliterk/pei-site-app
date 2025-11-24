import { Routes } from '@angular/router';
import { SettingsComponent } from './components/settings/settings.component';
import { HomeComponent } from './components/home/home.component';
import { StatisticsComponent } from './components/statistics/statistics.component';

export const routes: Routes = [
  { path: 'home', component: HomeComponent },
  { path: 'statistics', component: StatisticsComponent },
  { path: 'settings', component: SettingsComponent },
  { path: '', redirectTo: '/home', pathMatch: 'full' }
];
