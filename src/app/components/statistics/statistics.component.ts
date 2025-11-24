import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConnectionStatusService, ConnectionLog } from '../../services/connection-status.service';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { ChartModule } from 'primeng/chart';

@Component({
  selector: 'app-statistics',
  imports: [CommonModule, CardModule, TagModule, ProgressSpinnerModule, ChartModule],
  templateUrl: './statistics.component.html',
  styleUrl: './statistics.component.scss'
})
export class StatisticsComponent implements OnInit {
  connectionLogs: ConnectionLog[] = [];
  isLoading = false;

  // Chart data
  chartData: any;
  chartOptions: any;

  constructor(private connectionStatusService: ConnectionStatusService) {}

  async ngOnInit(): Promise<void> {
    await this.loadConnectionLogs();
    this.initChartOptions();
  }

  async loadConnectionLogs(): Promise<void> {
    this.isLoading = true;
    try {
      this.connectionLogs = await this.connectionStatusService.getConnectionLogs();
      this.buildChartData();
    } finally {
      this.isLoading = false;
    }
  }

  private initChartOptions(): void {
    this.chartOptions = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          display: true,
          position: 'top'
        }
      },
      scales: {
        x: {
          title: {
            display: true,
            text: 'Time'
          }
        },
        y: {
          title: {
            display: true,
            text: 'Status'
          },
          min: 0,
          max: 1,
          ticks: {
            stepSize: 1,
            callback: (value: number) => value === 1 ? 'Success' : 'Failed'
          }
        }
      }
    };
  }

  private buildChartData(): void {
    // Sort logs by timestamp (oldest first for chart display)
    const sortedLogs = [...this.connectionLogs].sort(
      (a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()
    );

    const labels = sortedLogs.map(log => {
      const date = new Date(log.timestamp);
      return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    });

    const data = sortedLogs.map(log => log.status === 'success' ? 1 : 0);

    // Color points based on status
    const pointColors = sortedLogs.map(log =>
      log.status === 'success' ? '#22c55e' : '#ef4444'
    );

    this.chartData = {
      labels,
      datasets: [
        {
          label: 'Connection Status',
          data,
          fill: false,
          borderColor: '#3b82f6',
          tension: 0,
          pointBackgroundColor: pointColors,
          pointBorderColor: pointColors,
          pointRadius: 4
        }
      ]
    };
  }

  getSuccessCount(): number {
    return this.connectionLogs.filter(log => log.status === 'success').length;
  }

  getFailureCount(): number {
    return this.connectionLogs.filter(log => log.status !== 'success').length;
  }

  getUptimePercentage(): number {
    if (this.connectionLogs.length === 0) return 0;
    return Math.round((this.getSuccessCount() / this.connectionLogs.length) * 100);
  }
}
