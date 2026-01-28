import { ApplicationConfig, provideZoneChangeDetection, APP_INITIALIZER } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { providePrimeNG } from 'primeng/config';
import Aura from '@primeng/themes/aura';

import { routes } from './app.routes';
import { environment } from '../environments/environment';

// Factory function to load settings from Electron before app initializes
function initializeAppSettings(): () => Promise<void> {
  return async () => {
    // Check if running in Electron
    if (window && (window as any).electronAPI) {
      const electronAPI = (window as any).electronAPI;

      try {
        // Load tenant ID
        const savedTenantId = await electronAPI.getTenantId();
        if (savedTenantId !== null) {
          environment.tenantId = typeof savedTenantId === 'string'
            ? parseInt(savedTenantId, 10)
            : savedTenantId;
        }

        // Load site number
        const savedSiteNumber = await electronAPI.getSiteNumber();
        if (savedSiteNumber !== null) {
          environment.siteNumber = savedSiteNumber;
        }

        // Load site name
        const savedSiteName = await electronAPI.getSiteName();
        if (savedSiteName !== null) {
          environment.siteName = savedSiteName;
        }

        console.log('Settings loaded from Electron store:', {
          tenantId: environment.tenantId,
          siteNumber: environment.siteNumber,
          siteName: environment.siteName
        });
      } catch (error) {
        console.error('Failed to load settings from Electron store:', error);
      }
    }
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(),
    provideAnimationsAsync(),
    providePrimeNG({
      theme: {
        preset: Aura,
        options: {
          darkModeSelector: false
        }
      }
    }),
    {
      provide: APP_INITIALIZER,
      useFactory: initializeAppSettings,
      multi: true
    }
  ]
};
