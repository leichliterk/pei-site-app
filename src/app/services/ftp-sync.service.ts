import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { ElectronService, FtpSettings, FtpSyncResult } from './electron.service';

export interface FtpSyncState {
  isRunning: boolean;
  lastSync: Date | null;
  lastResult: FtpSyncResult | null;
  nextSync: Date | null;
  isSyncing: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class FtpSyncService implements OnDestroy {
  private syncState$ = new BehaviorSubject<FtpSyncState>({
    isRunning: false,
    lastSync: null,
    lastResult: null,
    nextSync: null,
    isSyncing: false
  });

  private syncInterval: any = null;
  private settings: FtpSettings | null = null;

  constructor(private electronService: ElectronService) {
    this.initialize();
  }

  ngOnDestroy(): void {
    this.stopSync();
  }

  get state$(): Observable<FtpSyncState> {
    return this.syncState$.asObservable();
  }

  get currentState(): FtpSyncState {
    return this.syncState$.value;
  }

  /**
   * Initialize the service by loading settings and starting sync if enabled
   */
  private async initialize(): Promise<void> {
    await this.loadSettings();
    if (this.settings?.enabled) {
      this.startSync();
    }
  }

  /**
   * Load FTP settings from persistent storage
   */
  async loadSettings(): Promise<void> {
    this.settings = await this.electronService.getFtpSettings();
  }

  /**
   * Start the sync scheduler
   */
  async startSync(): Promise<void> {
    await this.loadSettings();

    if (!this.settings || !this.settings.host) {
      console.log('FTP sync not started: No settings configured');
      return;
    }

    // Stop any existing interval
    this.stopSync();

    const intervalMs = (this.settings.scheduleMinutes || 15) * 60 * 1000;

    // Update state
    this.updateState({
      isRunning: true,
      nextSync: new Date(Date.now() + intervalMs)
    });

    // Perform initial sync
    await this.performSync();

    // Set up interval for subsequent syncs
    this.syncInterval = setInterval(async () => {
      await this.performSync();
      this.updateState({
        nextSync: new Date(Date.now() + intervalMs)
      });
    }, intervalMs);

    console.log(`FTP sync started. Interval: ${this.settings.scheduleMinutes} minutes`);
  }

  /**
   * Stop the sync scheduler
   */
  stopSync(): void {
    if (this.syncInterval) {
      clearInterval(this.syncInterval);
      this.syncInterval = null;
    }

    this.updateState({
      isRunning: false,
      nextSync: null
    });

    console.log('FTP sync stopped');
  }

  /**
   * Perform a single sync operation
   */
  async performSync(): Promise<FtpSyncResult> {
    if (!this.settings || !this.settings.host) {
      const result: FtpSyncResult = {
        success: false,
        downloaded: [],
        errors: ['No FTP settings configured'],
        message: 'No FTP settings configured'
      };
      return result;
    }

    this.updateState({ isSyncing: true });

    try {
      const result = await this.electronService.ftpSyncFiles(
        this.settings.host,
        this.settings.path
      );

      this.updateState({
        isSyncing: false,
        lastSync: new Date(),
        lastResult: result
      });

      if (result.downloaded.length > 0) {
        console.log(`FTP sync completed: Downloaded ${result.downloaded.length} file(s)`);
      } else {
        console.log('FTP sync completed: No new files');
      }

      return result;
    } catch (error: any) {
      const result: FtpSyncResult = {
        success: false,
        downloaded: [],
        errors: [error.message || 'Unknown error'],
        message: error.message || 'Sync failed'
      };

      this.updateState({
        isSyncing: false,
        lastSync: new Date(),
        lastResult: result
      });

      console.error('FTP sync failed:', error);
      return result;
    }
  }

  /**
   * Manually trigger a sync
   */
  async manualSync(): Promise<FtpSyncResult> {
    await this.loadSettings();
    return this.performSync();
  }

  /**
   * Refresh settings and restart sync if needed
   */
  async refreshSettings(): Promise<void> {
    const wasRunning = this.currentState.isRunning;
    await this.loadSettings();

    if (this.settings?.enabled && !wasRunning) {
      await this.startSync();
    } else if (!this.settings?.enabled && wasRunning) {
      this.stopSync();
    } else if (this.settings?.enabled && wasRunning) {
      // Restart with new settings (e.g., new interval)
      await this.startSync();
    }
  }

  /**
   * Update the sync state
   */
  private updateState(partial: Partial<FtpSyncState>): void {
    this.syncState$.next({
      ...this.syncState$.value,
      ...partial
    });
  }
}
