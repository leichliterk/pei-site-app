import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil, interval } from 'rxjs';
import { FtpStatusComponent } from "../ftp-status/ftp-status.component";
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { WebSocketService, WebSocketStatus } from '../../services/websocket.service';
import { SiteService, UptimeResponse } from '../../services/site.service';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-home',
  imports: [CommonModule, FtpStatusComponent, CardModule, TagModule, TooltipModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent implements OnInit, OnDestroy {
  wsStatus: WebSocketStatus = WebSocketStatus.DISCONNECTED;
  WebSocketStatus = WebSocketStatus;
  connectedAt: Date | null = null;
  currentUptime: string = '--';
  uptimeData: UptimeResponse | null = null;
  uptimeLoading = false;
  uptimeError: string | null = null;
  private destroy$ = new Subject<void>();

  constructor(
    private webSocketService: WebSocketService,
    private siteService: SiteService
  ) {}

  ngOnInit(): void {
    this.webSocketService.connectionStatus$
      .pipe(takeUntil(this.destroy$))
      .subscribe(status => {
        const wasConnected = this.wsStatus === WebSocketStatus.CONNECTED;
        this.wsStatus = status;

        if (status === WebSocketStatus.CONNECTED && !wasConnected) {
          this.connectedAt = new Date();
        } else if (status !== WebSocketStatus.CONNECTED) {
          this.connectedAt = null;
          this.currentUptime = '--';
        }
      });

    // Update uptime every second
    interval(1000)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.updateUptime());

    // Fetch last 7 days connection status
    this.loadUptimeData();
  }

  private loadUptimeData(): void {
    this.uptimeLoading = true;
    this.uptimeError = null;

    this.siteService.getUptime(environment.tenantId, environment.siteNumber, 7)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          console.log('Uptime data:', data);
          this.uptimeData = data;
          this.uptimeLoading = false;
        },
        error: (err) => {
          console.error('Failed to load uptime data:', err);
          this.uptimeError = 'Failed to load uptime data';
          this.uptimeLoading = false;
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private updateUptime(): void {
    if (!this.connectedAt || this.wsStatus !== WebSocketStatus.CONNECTED) {
      this.currentUptime = '--';
      return;
    }

    const now = new Date();
    const diffMs = now.getTime() - this.connectedAt.getTime();
    this.currentUptime = this.formatDuration(diffMs);
  }

  private formatDuration(ms: number): string {
    const totalSeconds = Math.floor(ms / 1000);
    const days = Math.floor(totalSeconds / 86400);
    const hours = Math.floor((totalSeconds % 86400) / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${pad(days)}:${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
  }

  getStatusSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (this.wsStatus) {
      case WebSocketStatus.CONNECTED: return 'success';
      case WebSocketStatus.CONNECTING: return 'info';
      case WebSocketStatus.ERROR: return 'danger';
      default: return 'warn';
    }
  }

  getStatusIcon(): string {
    switch (this.wsStatus) {
      case WebSocketStatus.CONNECTED: return 'pi pi-check-circle';
      case WebSocketStatus.CONNECTING: return 'pi pi-spin pi-spinner';
      case WebSocketStatus.ERROR: return 'pi pi-times-circle';
      default: return 'pi pi-minus-circle';
    }
  }

  getStatusText(): string {
    switch (this.wsStatus) {
      case WebSocketStatus.CONNECTED: return 'Connected';
      case WebSocketStatus.CONNECTING: return 'Connecting...';
      case WebSocketStatus.ERROR: return 'Error';
      default: return 'Disconnected';
    }
  }
}
