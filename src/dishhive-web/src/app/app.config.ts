import { ApplicationConfig, provideBrowserGlobalErrorListeners, isDevMode } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideServiceWorker } from '@angular/service-worker';

import { routes } from './app.routes';
import { API_BASE_URL } from './api/generated/dishhive-api.client';
import { offlineReadInterceptor } from './services/offline-read-cache.service';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { localizedPaginator } from './services/material-labels';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptors([offlineReadInterceptor])),
    { provide: API_BASE_URL, useValue: '' },
    { provide: MatPaginatorIntl, useFactory: localizedPaginator },
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000'
    })
  ]
};
