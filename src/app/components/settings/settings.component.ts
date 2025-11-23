import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ConnectionStatusService } from '../../services/connection-status.service';
import { ElectronService, FtpSettings } from '../../services/electron.service';
import { FtpSyncService } from '../../services/ftp-sync.service';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';

// PrimeNG imports
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { ButtonModule } from 'primeng/button';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TabsModule } from 'primeng/tabs';

@Component({
  selector: 'app-settings',
  imports: [
    CommonModule,
    FormsModule,
    InputTextModule,
    InputNumberModule,
    ButtonModule,
    ToggleSwitchModule,
    DialogModule,
    MessageModule,
    TabsModule
  ],
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

  // Tab selection
  activeTab = '0';

  // FTP Settings
  ftpHost = '';
  ftpPath = '/';
  ftpScheduleMinutes = 15;
  ftpEnabled = false;
  ftpTestMessage = '';
  ftpTestSuccess: boolean | null = null;
  isFtpTesting = false;
  isFtpSaving = false;

  get isFormValid(): boolean {
    const inputStr = String(this.newSiteNumberInput);
    const parsedNumber = parseInt(inputStr, 10);
    const isNumberValid = !isNaN(parsedNumber) && inputStr.trim() !== '';
    const isPhraseValid = this.confirmationPhrase.toLowerCase() === 'change site number';
    return isNumberValid && isPhraseValid;
  }

  constructor(
    private connectionService: ConnectionStatusService,
    private electronService: ElectronService,
    private ftpSyncService: FtpSyncService,
    private route: ActivatedRoute
  ) {}

  async ngOnInit(): Promise<void> {
    // Check for tab query parameter
    this.route.queryParams.subscribe(params => {
      if (params['tab']) {
        this.activeTab = params['tab'];
      }
    });

    this.subscription = this.connectionService.isTimerRunning$.subscribe(
      isRunning => this.isTimerRunning = isRunning
    );

    // Load saved site number from Electron store
    const savedSiteNumber = await this.electronService.getSiteNumber();
    if (savedSiteNumber !== null) {
      this.siteNumber = savedSiteNumber;
      environment.siteNumber = savedSiteNumber;
    }

    // Load FTP settings
    await this.loadFtpSettings();
  }

  private async loadFtpSettings(): Promise<void> {
    const ftpSettings = await this.electronService.getFtpSettings();
    if (ftpSettings) {
      this.ftpHost = ftpSettings.host || '';
      this.ftpPath = ftpSettings.path || '/';
      this.ftpScheduleMinutes = ftpSettings.scheduleMinutes || 15;
      this.ftpEnabled = ftpSettings.enabled || false;
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

  // FTP Methods
  async testFtpConnection(): Promise<void> {
    if (!this.ftpHost) {
      this.ftpTestMessage = 'Please enter an FTP host address';
      this.ftpTestSuccess = false;
      return;
    }

    this.isFtpTesting = true;
    this.ftpTestMessage = '';
    this.ftpTestSuccess = null;

    try {
      const result = await this.electronService.ftpTestConnection(this.ftpHost, this.ftpPath);
      this.ftpTestMessage = result.message;
      this.ftpTestSuccess = result.success;
    } catch (error: any) {
      this.ftpTestMessage = error.message || 'Failed to test connection';
      this.ftpTestSuccess = false;
    } finally {
      this.isFtpTesting = false;
    }
  }

  async saveFtpSettings(): Promise<void> {
    this.isFtpSaving = true;

    try {
      const settings: FtpSettings = {
        host: this.ftpHost,
        path: this.ftpPath,
        scheduleMinutes: this.ftpScheduleMinutes,
        enabled: this.ftpEnabled
      };

      const success = await this.electronService.setFtpSettings(settings);
      if (success) {
        // Refresh the FTP sync service with new settings
        await this.ftpSyncService.refreshSettings();
        this.ftpTestMessage = 'FTP settings saved successfully';
        this.ftpTestSuccess = true;
      } else {
        this.ftpTestMessage = 'Failed to save FTP settings';
        this.ftpTestSuccess = false;
      }
    } catch (error: any) {
      this.ftpTestMessage = error.message || 'Failed to save settings';
      this.ftpTestSuccess = false;
    } finally {
      this.isFtpSaving = false;
    }
  }

  async toggleFtpEnabled(): Promise<void> {
    this.ftpEnabled = !this.ftpEnabled;
    await this.saveFtpSettings();
  }
}
