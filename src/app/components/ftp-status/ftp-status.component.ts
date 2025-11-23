import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subject, takeUntil, combineLatest } from 'rxjs';
import { FtpStatusService, FtpStatus, FtpState } from '../../services/ftp-status.service';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';

@Component({
  selector: 'app-ftp-status',
  imports: [CommonModule, TagModule, TooltipModule],
  templateUrl: './ftp-status.component.html',
  styleUrl: './ftp-status.component.scss'
})
export class FtpStatusComponent implements OnInit, OnDestroy {
  ftpState: FtpState = {
    status: FtpStatus.DISABLED,
    lastChecked: new Date()
  };

  FtpStatus = FtpStatus;
  private destroy$ = new Subject<void>();
  private hasBeenConnected = false;
  isTimerRunning = true;

  constructor(
    private ftpStatusService: FtpStatusService,
    private router: Router
  ) {}

  ngOnInit(): void {
    combineLatest([
      this.ftpStatusService.status$,
      this.ftpStatusService.isTimerRunning$
    ])
      .pipe(takeUntil(this.destroy$))
      .subscribe(([state, isTimerRunning]) => {
        this.ftpState = state;
        this.isTimerRunning = isTimerRunning;

        if (state.status === FtpStatus.CONNECTED) {
          this.hasBeenConnected = true;
        }

        if (state.status === FtpStatus.ERROR || state.status === FtpStatus.DISCONNECTED) {
          this.hasBeenConnected = false;
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onStatusClick(): void {
    if (this.ftpState.status === FtpStatus.DISABLED) {
      // Navigate to settings FTP tab when disabled
      this.router.navigate(['/settings'], { queryParams: { tab: '2' } });
    } else {
      // Retry connection
      this.ftpStatusService.retryConnection();
    }
  }

  getStatusText(): string {
    if (this.hasBeenConnected && this.ftpState.status === FtpStatus.CONNECTING) {
      return 'FTP Connected';
    }

    switch (this.ftpState.status) {
      case FtpStatus.CONNECTED:
        return 'FTP Connected';
      case FtpStatus.CONNECTING:
        return 'FTP Connecting...';
      case FtpStatus.DISCONNECTED:
        return 'FTP Disconnected';
      case FtpStatus.DISABLED:
        return 'FTP Disabled';
      case FtpStatus.ERROR:
        return 'FTP Error';
      default:
        return 'FTP Unknown';
    }
  }

  getTagSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast' | undefined {
    if (this.hasBeenConnected && this.ftpState.status === FtpStatus.CONNECTING) {
      return 'success';
    }

    switch (this.ftpState.status) {
      case FtpStatus.CONNECTED:
        return 'success';
      case FtpStatus.CONNECTING:
        return 'info';
      case FtpStatus.DISCONNECTED:
        return 'warn';
      case FtpStatus.DISABLED:
        return 'secondary';
      case FtpStatus.ERROR:
        return 'danger';
      default:
        return 'secondary';
    }
  }

  getTagIcon(): string {
    switch (this.ftpState.status) {
      case FtpStatus.CONNECTED:
        return 'pi pi-check-circle';
      case FtpStatus.CONNECTING:
        return 'pi pi-spin pi-spinner';
      case FtpStatus.DISCONNECTED:
        return 'pi pi-minus-circle';
      case FtpStatus.DISABLED:
        return 'pi pi-power-off';
      case FtpStatus.ERROR:
        return 'pi pi-exclamation-circle';
      default:
        return 'pi pi-question-circle';
    }
  }

  getTooltip(): string {
    if (this.ftpState.status === FtpStatus.DISABLED) {
      return 'Click to configure FTP settings';
    }
    if (this.ftpState.message) {
      return `${this.ftpState.message} - Click to refresh`;
    }
    return 'Click to refresh FTP status';
  }
}
