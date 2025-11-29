import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ConnectionStatusService, ConnectionStatus, ConnectionState } from '../../services/connection-status.service';
import { ElectronService, FtpSettings } from '../../services/electron.service';
import { FtpSyncService } from '../../services/ftp-sync.service';
import { SiteService } from '../../services/site.service';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import { TagModule } from 'primeng/tag';

// PrimeNG imports
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { ButtonModule } from 'primeng/button';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TabsModule } from 'primeng/tabs';
import { ToastModule } from 'primeng/toast';
import { CardModule } from 'primeng/card';
import { MessageService } from 'primeng/api';

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
    TabsModule,
    TagModule,
    ToastModule,
    CardModule
  ],
  providers: [MessageService],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent implements OnInit, OnDestroy {
  isTimerRunning = false;
  siteNumber = environment.siteNumber;
  siteName = '';
  tenantId = '';
  showDialog = false;
  newSiteNumberInput = '';
  confirmationPhrase = '';
  errorMessage = '';
  showTenantDialog = false;
  newTenantIdInput = '';
  tenantConfirmationPhrase = '';
  tenantErrorMessage = '';
  private subscription?: Subscription;
  private connectionSubscription?: Subscription;

  // Connection status
  connectionState: ConnectionState = {
    status: ConnectionStatus.DISCONNECTED,
    lastChecked: new Date()
  };
  ConnectionStatus = ConnectionStatus;

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

  get isTenantFormValid(): boolean {
    const isTenantIdValid = this.newTenantIdInput.trim() !== '';
    const isPhraseValid = this.tenantConfirmationPhrase.toLowerCase() === 'change tenant id';
    return isTenantIdValid && isPhraseValid;
  }

  constructor(
    private connectionService: ConnectionStatusService,
    private electronService: ElectronService,
    private ftpSyncService: FtpSyncService,
    private siteService: SiteService,
    private messageService: MessageService,
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

    this.connectionSubscription = this.connectionService.status$.subscribe(
      state => this.connectionState = state
    );

    // Load saved site number from Electron store
    const savedSiteNumber = await this.electronService.getSiteNumber();
    if (savedSiteNumber !== null) {
      this.siteNumber = savedSiteNumber;
      environment.siteNumber = savedSiteNumber;
    }

    // Load saved site name from Electron store
    const savedSiteName = await this.electronService.getSiteName();
    if (savedSiteName !== null) {
      this.siteName = savedSiteName;
    }

    // Load saved tenant ID from Electron store
    const savedTenantId = await this.electronService.getTenantId();
    if (savedTenantId !== null) {
      this.tenantId = savedTenantId;
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
    if (this.connectionSubscription) {
      this.connectionSubscription.unsubscribe();
    }
  }

  toggleConnectionTimer(): void {
    this.connectionService.toggleTimer();
  }

  getConnectionStatusText(): string {
    switch (this.connectionState.status) {
      case ConnectionStatus.CONNECTED:
        return 'Connected';
      case ConnectionStatus.CONNECTING:
        return 'Connecting...';
      case ConnectionStatus.DISCONNECTED:
        return 'Disconnected';
      case ConnectionStatus.ERROR:
        return 'Error';
      default:
        return 'Unknown';
    }
  }

  getConnectionTagSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
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

  getConnectionTagIcon(): string {
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

  editTenantId(): void {
    this.showTenantDialog = true;
    this.newTenantIdInput = this.tenantId;
    this.tenantConfirmationPhrase = '';
    this.tenantErrorMessage = '';
  }

  closeTenantDialog(): void {
    this.showTenantDialog = false;
    this.newTenantIdInput = '';
    this.tenantConfirmationPhrase = '';
    this.tenantErrorMessage = '';
  }

  async confirmTenantIdChange(): Promise<void> {
    console.log('Confirm tenant ID change clicked');
    console.log('New tenant ID input:', this.newTenantIdInput);
    console.log('Confirmation phrase:', this.tenantConfirmationPhrase);
    console.log('Is form valid:', this.isTenantFormValid);

    this.tenantErrorMessage = '';

    // Validate tenant ID
    if (this.newTenantIdInput.trim() === '') {
      this.tenantErrorMessage = 'Please enter a valid tenant ID';
      console.log('Validation failed: empty tenant ID');
      return;
    }

    // Validate confirmation phrase
    if (this.tenantConfirmationPhrase.toLowerCase() !== 'change tenant id') {
      this.tenantErrorMessage = 'Please type "change tenant id" to confirm';
      console.log('Validation failed: incorrect phrase');
      return;
    }

    // Update tenant ID
    console.log('Updating tenant ID from', this.tenantId, 'to', this.newTenantIdInput);
    this.tenantId = this.newTenantIdInput;

    // Persist to Electron store
    const success = await this.electronService.setTenantId(this.tenantId);
    if (success) {
      console.log('Tenant ID saved to persistent storage successfully');
    } else {
      console.log('Failed to save tenant ID to persistent storage (may be running in browser mode)');
    }

    this.closeTenantDialog();
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

    // Store old site number for API call
    const oldSiteNumber = this.siteNumber;

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

    // Update site ID on the server
    if (this.tenantId && oldSiteNumber) {
      this.siteService.updateSiteId(this.tenantId, oldSiteNumber.toString(), parsedNumber.toString()).subscribe({
        next: (response) => {
          console.log('Site ID updated successfully on server:', response);
        },
        error: (error) => {
          console.error('Failed to update site ID on server:', error);
          this.errorMessage = 'Site number saved locally, but failed to update on server. Please check your connection.';
        }
      });
    } else {
      console.warn('Cannot update site ID on server: missing tenant ID');
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

  async saveSiteName(): Promise<void> {
    try {
      // Validate we have required information
      if (!this.tenantId || !this.siteNumber) {
        console.warn('Cannot update site name on server: missing tenant ID or site number');
        this.messageService.add({
          severity: 'warn',
          summary: 'Missing Information',
          detail: 'Cannot update on server: missing tenant ID or site number',
          life: 4000
        });
        return;
      }

      // Update site name on the server first
      this.siteService.updateSiteName(this.tenantId, this.siteNumber.toString(), this.siteName).subscribe({
        next: async (response) => {
          console.log('Site name updated successfully on server:', response);

          // Update the environment variable so toolbar reflects the change
          environment.siteName = this.siteName;

          // Only update local storage after successful API call
          const success = await this.electronService.setSiteName(this.siteName);
          if (!success) {
            console.log('Failed to save site name to persistent storage (may be running in browser mode)');
            this.messageService.add({
              severity: 'warn',
              summary: 'Partial Success',
              detail: 'Site name updated on server but failed to save locally',
              life: 4000
            });
          } else {
            this.messageService.add({
              severity: 'success',
              summary: 'Success',
              detail: 'Site name updated successfully',
              life: 3000
            });
          }
        },
        error: (error) => {
          console.error('Failed to update site name on server:', error);
          this.messageService.add({
            severity: 'error',
            summary: 'Update Failed',
            detail: 'Failed to update site name on server',
            life: 5000
          });
        }
      });
    } catch (error) {
      console.error('Error saving site name:', error);
      this.messageService.add({
        severity: 'error',
        summary: 'Error',
        detail: 'An unexpected error occurred while saving',
        life: 5000
      });
    }
  }
}
