import { ApplicationRef, Injectable, signal } from '@angular/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { MatSnackBar } from '@angular/material/snack-bar';
import { concat, filter, first, interval } from 'rxjs';

/**
 * PWA lifecycle glue (Freezy's service-worker pattern): notifies on new app
 * versions, tracks online/offline, and exposes the browser install prompt.
 * Instantiated once from the App component; harmless when the service worker
 * is disabled (dev mode / unsupported browsers).
 */
@Injectable({ providedIn: 'root' })
export class PwaService {
  readonly isOnline = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  readonly canInstall = signal(false);

  // BeforeInstallPromptEvent is Chromium-only and not in the TS DOM lib
  private deferredInstallPrompt: any = null;

  constructor(
    private swUpdate: SwUpdate,
    private appRef: ApplicationRef,
    private snackBar: MatSnackBar
  ) {
    this.initUpdateChecks();
    this.initOnlineStatus();
    this.initInstallPrompt();
  }

  private initUpdateChecks(): void {
    if (!this.swUpdate.isEnabled) {
      return;
    }

    // Check once the app is stable, then every 6 hours (Angular docs pattern)
    const appIsStable$ = this.appRef.isStable.pipe(first(stable => stable));
    const everySixHours$ = interval(6 * 60 * 60 * 1000);
    concat(appIsStable$, everySixHours$).subscribe(() => {
      this.swUpdate.checkForUpdate().catch(() => { /* offline or SW gone */ });
    });

    this.swUpdate.versionUpdates
      .pipe(filter((evt): evt is VersionReadyEvent => evt.type === 'VERSION_READY'))
      .subscribe(() => {
        this.snackBar
          .open('A new version of Dishhive is available', 'Update', { duration: 0 })
          .onAction()
          .subscribe(() => this.reloadToUpdate());
      });

    this.swUpdate.unrecoverable.subscribe(() => {
      this.snackBar
        .open('The app needs to reload to recover', 'Reload', { duration: 0 })
        .onAction()
        .subscribe(() => window.location.reload());
    });
  }

  reloadToUpdate(): void {
    this.swUpdate.activateUpdate().then(() => window.location.reload());
  }

  private initOnlineStatus(): void {
    window.addEventListener('online', () => {
      this.isOnline.set(true);
      this.snackBar.open('You are back online', 'Dismiss', { duration: 3000 });
    });

    window.addEventListener('offline', () => {
      this.isOnline.set(false);
      this.snackBar.open('You are offline — showing saved data where possible', 'Dismiss', {
        duration: 5000
      });
    });
  }

  private initInstallPrompt(): void {
    window.addEventListener('beforeinstallprompt', (event: Event) => {
      event.preventDefault();
      this.deferredInstallPrompt = event;
      this.canInstall.set(true);
    });

    window.addEventListener('appinstalled', () => {
      this.deferredInstallPrompt = null;
      this.canInstall.set(false);
    });
  }

  async promptInstall(): Promise<boolean> {
    if (!this.deferredInstallPrompt) {
      return false;
    }
    this.deferredInstallPrompt.prompt();
    const { outcome } = await this.deferredInstallPrompt.userChoice;
    this.deferredInstallPrompt = null;
    this.canInstall.set(false);
    return outcome === 'accepted';
  }
}
