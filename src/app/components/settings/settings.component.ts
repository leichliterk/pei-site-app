import { Component, OnInit, OnDestroy } from '@angular/core';
import { ConnectionStatusService } from '../../services/connection-status.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-settings',
  imports: [],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent implements OnInit, OnDestroy {
  isTimerRunning = false;
  private subscription?: Subscription;

  constructor(private connectionService: ConnectionStatusService) {}

  ngOnInit(): void {
    this.subscription = this.connectionService.isTimerRunning$.subscribe(
      isRunning => this.isTimerRunning = isRunning
    );
  }

  ngOnDestroy(): void {
    if (this.subscription) {
      this.subscription.unsubscribe();
    }
  }

  toggleConnectionTimer(): void {
    this.connectionService.toggleTimer();
  }
}
