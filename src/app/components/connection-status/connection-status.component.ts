import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subject, takeUntil, combineLatest } from 'rxjs';
import { ConnectionStatusService, ConnectionStatus, ConnectionState } from '../../services/connection-status.service';

@Component({
  selector: 'app-connection-status',
  imports: [CommonModule],
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
      // Navigate to settings when heartbeat is stopped
      this.router.navigate(['/settings']).then(() => {
        // Focus on the specific connection timer toggle after navigation
        setTimeout(() => {
          const toggleInput = document.getElementById('connection-timer-toggle') as HTMLInputElement;
          if (toggleInput) {
            const settingItem = toggleInput.closest('.setting-item') as HTMLElement;
            if (settingItem) {
              // Add highlight effect
              settingItem.classList.add('highlight');

              // Scroll into view
              settingItem.scrollIntoView({ behavior: 'smooth', block: 'center' });

              // Focus on the toggle
              toggleInput.focus();

              // Remove highlight after animation
              setTimeout(() => {
                settingItem.classList.remove('highlight');
              }, 2000);
            }
          }
        }, 200);
      });
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
}
