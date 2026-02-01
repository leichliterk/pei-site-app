import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil, interval } from 'rxjs';
import { FtpStatusComponent } from "../ftp-status/ftp-status.component";
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { ChartModule } from 'primeng/chart';
import { WebSocketService, WebSocketStatus } from '../../services/websocket.service';
import { SiteService, UptimeResponse } from '../../services/site.service';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-home',
  imports: [CommonModule, FtpStatusComponent, CardModule, TagModule, TooltipModule, ChartModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent implements OnInit, OnDestroy {
  wsStatus: WebSocketStatus = WebSocketStatus.DISCONNECTED;
  WebSocketStatus = WebSocketStatus;
  currentUptime: string = '--';
  uptimeData: UptimeResponse | null = null;
  uptimeLoading = false;
  uptimeError: string | null = null;
  private destroy$ = new Subject<void>();

  // Chart data
  chartData: any;
  chartOptions: any;

  constructor(
    private webSocketService: WebSocketService,
    private siteService: SiteService
  ) {}

  get connectedAt(): Date | null {
    return this.webSocketService.connectedAt;
  }

  ngOnInit(): void {
    this.initChart();

    this.webSocketService.connectionStatus$
      .pipe(takeUntil(this.destroy$))
      .subscribe(status => {
        this.wsStatus = status;
        if (status !== WebSocketStatus.CONNECTED) {
          this.currentUptime = '--';
        }
      });

    // Update uptime and chart every second
    interval(1000)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.updateUptime();
        this.updateChartData();
      });

    // Fetch last 7 days connection status
    this.loadUptimeData();
  }

  private initChart(): void {
    this.chartOptions = {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      plugins: {
        legend: {
          display: false
        },
        tooltip: {
          enabled: false
        }
      },
      scales: {
        x: {
          display: true,
          title: {
            display: false
          },
          ticks: {
            display: true,
            maxTicksLimit: 6,
            callback: (_value: number, index: number) => {
              const secondsAgo = 300 - index;
              if (secondsAgo === 300) return '-5m';
              if (secondsAgo === 240) return '-4m';
              if (secondsAgo === 180) return '-3m';
              if (secondsAgo === 120) return '-2m';
              if (secondsAgo === 60) return '-1m';
              if (secondsAgo === 0) return 'Now';
              return '';
            }
          },
          grid: {
            display: false
          }
        },
        y: {
          display: true,
          min: 0,
          max: 1,
          ticks: {
            stepSize: 1,
            callback: (value: number) => value === 1 ? 'Up' : 'Down'
          },
          grid: {
            display: false
          }
        }
      }
    };

    this.updateChartData();
  }

  private updateChartData(): void {
    this.chartData = {
      labels: Array.from({ length: 300 }, (_, i) => i),
      datasets: [
        {
          data: [...this.webSocketService.connectionHistory],
          fill: true,
          backgroundColor: 'rgba(34, 197, 94, 0.2)',
          borderColor: 'rgb(34, 197, 94)',
          borderWidth: 2,
          tension: 0,
          pointRadius: 0,
          stepped: true
        }
      ]
    };
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

          // Prepopulate connection history with actual session data
          if (data.sessions && data.sessions.length > 0) {
            this.webSocketService.prepopulateHistory(data.sessions);
            this.updateChartData();
          }
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
