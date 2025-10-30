import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConnectionStatusService } from '../../services/connection-status.service';
import { ElectronService } from '../../services/electron.service';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-settings',
  imports: [CommonModule, FormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent implements OnInit, OnDestroy {
  isTimerRunning = false;
  siteNumber = environment.siteNumber;
  showDialog = false;
  newSiteNumberInput = '';
  confirmationPhrase = '';
  errorMessage = '';
  private subscription?: Subscription;

  get isFormValid(): boolean {
    const inputStr = String(this.newSiteNumberInput);
    const parsedNumber = parseInt(inputStr, 10);
    const isNumberValid = !isNaN(parsedNumber) && inputStr.trim() !== '';
    const isPhraseValid = this.confirmationPhrase.toLowerCase() === 'change site number';
    return isNumberValid && isPhraseValid;
  }

  constructor(
    private connectionService: ConnectionStatusService,
    private electronService: ElectronService
  ) {}

  async ngOnInit(): Promise<void> {
    this.subscription = this.connectionService.isTimerRunning$.subscribe(
      isRunning => this.isTimerRunning = isRunning
    );

    // Load saved site number from Electron store
    const savedSiteNumber = await this.electronService.getSiteNumber();
    if (savedSiteNumber !== null) {
      this.siteNumber = savedSiteNumber;
      environment.siteNumber = savedSiteNumber;
    }
  }

  ngOnDestroy(): void {
    if (this.subscription) {
      this.subscription.unsubscribe();
    }
  }

  toggleConnectionTimer(): void {
    this.connectionService.toggleTimer();
  }

  editSiteNumber(): void {
    this.showDialog = true;
    this.newSiteNumberInput = this.siteNumber.toString();
    this.confirmationPhrase = '';
    this.errorMessage = '';
  }

  closeDialog(): void {
    this.showDialog = false;
    this.newSiteNumberInput = '';
    this.confirmationPhrase = '';
    this.errorMessage = '';
  }

  async confirmSiteNumberChange(): Promise<void> {
    console.log('Confirm clicked');
    console.log('New site number input:', this.newSiteNumberInput);
    console.log('Confirmation phrase:', this.confirmationPhrase);
    console.log('Is form valid:', this.isFormValid);

    this.errorMessage = '';

    // Validate site number
    const inputStr = String(this.newSiteNumberInput);
    const parsedNumber = parseInt(inputStr, 10);
    if (isNaN(parsedNumber) || inputStr.trim() === '') {
      this.errorMessage = 'Please enter a valid number';
      console.log('Validation failed: invalid number');
      return;
    }

    // Validate confirmation phrase
    if (this.confirmationPhrase.toLowerCase() !== 'change site number') {
      this.errorMessage = 'Please type "change site number" to confirm';
      console.log('Validation failed: incorrect phrase');
      return;
    }

    // Update site number
    console.log('Updating site number from', this.siteNumber, 'to', parsedNumber);
    this.siteNumber = parsedNumber;
    environment.siteNumber = parsedNumber;

    // Persist to Electron store
    const success = await this.electronService.setSiteNumber(parsedNumber);
    if (success) {
      console.log('Site number saved to persistent storage successfully');
    } else {
      console.log('Failed to save site number to persistent storage (may be running in browser mode)');
    }

    this.closeDialog();
  }
}
