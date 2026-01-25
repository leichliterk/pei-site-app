import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { FtpStatusComponent } from "../ftp-status/ftp-status.component";
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { WebSocketService, WebSocketStatus } from '../../services/websocket.service';

@Component({
  selector: 'app-home',
  imports: [CommonModule, FtpStatusComponent, CardModule, TagModule, TooltipModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent implements OnInit, OnDestroy {
  wsStatus: WebSocketStatus = WebSocketStatus.DISCONNECTED;
  WebSocketStatus = WebSocketStatus;
  private destroy$ = new Subject<void>();

  constructor(private webSocketService: WebSocketService) {}

  ngOnInit(): void {
    this.webSocketService.connectionStatus$
      .pipe(takeUntil(this.destroy$))
      .subscribe(status => this.wsStatus = status);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
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
