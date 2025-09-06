import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
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

  constructor(private connectionStatusService: ConnectionStatusService) {}

  ngOnInit(): void {
    this.connectionStatusService.status$
      .pipe(takeUntil(this.destroy$))
      .subscribe(state => {
        this.connectionState = state;
        
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

  onRetryConnection(): void {
    this.connectionStatusService.retryConnection();
  }

  getStatusText(): string {
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
    return `status-${this.connectionState.status}`;
  }
}
