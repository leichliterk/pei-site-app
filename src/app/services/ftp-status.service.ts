import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { ElectronService } from './electron.service';

export enum FtpStatus {
  CONNECTED = 'connected',
  CONNECTING = 'connecting',
  DISCONNECTED = 'disconnected',
  DISABLED = 'disabled',
  ERROR = 'error'
}

export interface FtpState {
  status: FtpStatus;
  lastChecked: Date;
  message?: string;
  host?: string;
}

@Injectable({
  providedIn: 'root'
})
export class FtpStatusService implements OnDestroy {
  private statusSubject = new BehaviorSubject<FtpState>({
    status: FtpStatus.DISABLED,
    lastChecked: new Date()
  });

  private timerRunningSubject = new BehaviorSubject<boolean>(true);
  private checkInterval: any = null;
  private readonly CHECK_INTERVAL_MS = 15000; // 15 seconds

  status$ = this.statusSubject.asObservable();
  isTimerRunning$ = this.timerRunningSubject.asObservable();

  constructor(private electronService: ElectronService) {
    this.initialize();
  }

  private async initialize(): Promise<void> {
    await this.checkFtpStatus();
    this.startTimer();
  }

  private startTimer(): void {
    if (this.checkInterval) {
      clearInterval(this.checkInterval);
    }
    this.checkInterval = setInterval(() => {
      this.checkFtpStatus();
    }, this.CHECK_INTERVAL_MS);
    this.timerRunningSubject.next(true);
  }

  private stopTimer(): void {
    if (this.checkInterval) {
      clearInterval(this.checkInterval);
      this.checkInterval = null;
    }
    this.timerRunningSubject.next(false);
  }

  toggleTimer(): void {
    if (this.timerRunningSubject.value) {
      this.stopTimer();
    } else {
      this.startTimer();
      this.checkFtpStatus();
    }
  }

  async checkFtpStatus(): Promise<void> {
    // Get FTP settings first
    const settings = await this.electronService.getFtpSettings();

    // If FTP is not enabled or no host configured, show disabled status
    if (!settings || !settings.enabled || !settings.host) {
      this.statusSubject.next({
        status: FtpStatus.DISABLED,
        lastChecked: new Date(),
        message: !settings?.host ? 'No FTP host configured' : 'FTP sync disabled'
      });
      return;
    }

    // Set connecting status
    this.statusSubject.next({
      status: FtpStatus.CONNECTING,
      lastChecked: new Date(),
      host: settings.host
    });

    try {
      const result = await this.electronService.ftpTestConnection(settings.host, settings.path);

      if (result.success) {
        this.statusSubject.next({
          status: FtpStatus.CONNECTED,
          lastChecked: new Date(),
          message: result.message,
          host: settings.host
        });
      } else {
        this.statusSubject.next({
          status: FtpStatus.ERROR,
          lastChecked: new Date(),
          message: result.message,
          host: settings.host
        });
      }
    } catch (error: any) {
      this.statusSubject.next({
        status: FtpStatus.ERROR,
        lastChecked: new Date(),
        message: error.message || 'Failed to connect to FTP server',
        host: settings.host
      });
    }
  }

  retryConnection(): void {
    this.checkFtpStatus();
  }

  ngOnDestroy(): void {
    this.stopTimer();
  }
}
