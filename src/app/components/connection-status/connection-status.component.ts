import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subject, takeUntil, combineLatest } from 'rxjs';
import { ConnectionStatusService, ConnectionStatus, ConnectionState } from '../../services/connection-status.service';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';

@Component({
  selector: 'app-connection-status',
  imports: [CommonModule, TagModule, TooltipModule],
  templateUrl: './connection-status.component.html',
  styleUrl: './connection-status.component.scss'
})
export class ConnectionStatusComponent implements OnInit, OnDestroy {
  connectionState: ConnectionState = {
    status: ConnectionStatus.DISCONNECTED,
    lastChecked: new Date()
  };

  ConnectionStatus = ConnectionStatus;
  private destroy$ = new Subject<void>();
  private hasBeenConnected = false;
  isTimerRunning = true;

  constructor(
    private connectionStatusService: ConnectionStatusService,
    private router: Router
  ) {}

  ngOnInit(): void {
    // Subscribe to both connection status and timer state
    combineLatest([
      this.connectionStatusService.status$,
      this.connectionStatusService.isTimerRunning$
    ])
      .pipe(takeUntil(this.destroy$))
      .subscribe(([state, isTimerRunning]) => {
        this.connectionState = state;
        this.isTimerRunning = isTimerRunning;

        // Track when connection first becomes successful
        if (state.status === ConnectionStatus.CONNECTED) {
          this.hasBeenConnected = true;
        }

        // Reset flag if connection fails
        if (state.status === ConnectionStatus.ERROR || state.status === ConnectionStatus.DISCONNECTED) {
          this.hasBeenConnected = false;
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onStatusClick(): void {
    if (!this.isTimerRunning) {
      // Navigate to settings Heartbeat tab when heartbeat is stopped
      this.router.navigate(['/settings'], { queryParams: { tab: '1' } });
    } else {
      // Normal retry connection behavior
      this.connectionStatusService.retryConnection();
    }
  }

  getStatusText(): string {
    // If timer is stopped, show heartbeat stopped message
    if (!this.isTimerRunning) {
      return 'Heartbeat stopped';
    }

    // Once connected successfully, keep showing "Connected" unless there's an actual failure
    if (this.hasBeenConnected && this.connectionState.status === ConnectionStatus.CONNECTING) {
      return 'Connected to Server';
    }

    switch (this.connectionState.status) {
      case ConnectionStatus.CONNECTED:
        return 'Connected to Server';
      case ConnectionStatus.CONNECTING:
        return 'Connecting...';
      case ConnectionStatus.DISCONNECTED:
        return 'Disconnected from Server';
      case ConnectionStatus.ERROR:
        return 'Connection Error';
      default:
        return 'Unknown Status';
    }
  }

  getStatusClass(): string {
    // If timer is stopped, use stopped class for red styling
    if (!this.isTimerRunning) {
      return 'status-stopped';
    }
    return `status-${this.connectionState.status}`;
  }

  getTagSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast' | undefined {
    if (!this.isTimerRunning) {
      return 'danger';
    }

    if (this.hasBeenConnected && this.connectionState.status === ConnectionStatus.CONNECTING) {
      return 'success';
    }

    switch (this.connectionState.status) {
      case ConnectionStatus.CONNECTED:
        return 'success';
      case ConnectionStatus.CONNECTING:
        return 'info';
      case ConnectionStatus.DISCONNECTED:
        return 'warn';
      case ConnectionStatus.ERROR:
        return 'danger';
      default:
        return 'secondary';
    }
  }

  getTagIcon(): string {
    if (!this.isTimerRunning) {
      return 'pi pi-stop-circle';
    }

    switch (this.connectionState.status) {
      case ConnectionStatus.CONNECTED:
        return 'pi pi-check-circle';
      case ConnectionStatus.CONNECTING:
        return 'pi pi-spin pi-spinner';
      case ConnectionStatus.DISCONNECTED:
        return 'pi pi-minus-circle';
      case ConnectionStatus.ERROR:
        return 'pi pi-exclamation-circle';
      default:
        return 'pi pi-question-circle';
    }
  }
}
