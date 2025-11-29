import { Component, OnInit } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { ElectronService } from './services/electron.service';
import { FtpSyncService } from './services/ftp-sync.service';
import { environment } from '../environments/environment';
import { ButtonModule } from 'primeng/button';
import { ToolbarModule } from 'primeng/toolbar';
import { TooltipModule } from 'primeng/tooltip';
import { MessageModule } from 'primeng/message';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ButtonModule, ToolbarModule, TooltipModule, MessageModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit {
  selectedFile: any = null;
  isLoading = false;
  error: string | null = null;
  environment = environment;

  constructor(
    public electronService: ElectronService,
    private router: Router,
    private ftpSyncService: FtpSyncService  // Inject to initialize the service
  ) {}

  async ngOnInit(): Promise<void> {
    // Load saved site number from Electron store
    const savedSiteNumber = await this.electronService.getSiteNumber();
    if (savedSiteNumber !== null) {
      environment.siteNumber = savedSiteNumber;
    }

    // Load saved site name from Electron store
    const savedSiteName = await this.electronService.getSiteName();
    if (savedSiteName !== null) {
      environment.siteName = savedSiteName;
    }
  }

  async openFile() {
    if (!this.electronService.isElectronApp) {
      this.error = 'File system access requires Electron';
      return;
    }

    this.isLoading = true;
    this.error = null;

    try {
      const result = await this.electronService.openFile();
      if (result && !result.canceled && result.filePaths.length > 0) {
        // Create file info object
        this.selectedFile = {
          name: result.filePaths[0].split(/[\\/]/).pop(),
          path: result.filePaths[0],
          size: result.fileSize || 0,
          type: this.getFileType(result.filePaths[0]),
          lastModified: new Date().toISOString()
        };
      }
    } catch (error) {
      this.error = `Failed to open file: ${error}`;
      console.error('Error opening file:', error);
    } finally {
      this.isLoading = false;
    }
  }

  formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  getFileType(filePath: string): string {
    const extension = filePath.split('.').pop()?.toLowerCase();
    const types: { [key: string]: string } = {
      'txt': 'Text File',
      'pdf': 'PDF Document',
      'jpg': 'JPEG Image',
      'jpeg': 'JPEG Image',
      'png': 'PNG Image',
      'gif': 'GIF Image',
      'mp4': 'MP4 Video',
      'mp3': 'MP3 Audio',
      'zip': 'ZIP Archive',
      'json': 'JSON Data',
      'xml': 'XML Document',
      'html': 'HTML Document',
      'css': 'CSS Stylesheet',
      'js': 'JavaScript File',
      'ts': 'TypeScript File'
    };
    return types[extension || ''] || `${extension?.toUpperCase()} File` || 'Unknown';
  }

  clearError() {
    this.error = null;
  }

  navigateToSettings() {
    this.router.navigate(['/settings']);
  }

  navigateToHome() {
    this.router.navigate(['/home']);
  }

  navigateToStatistics() {
    this.router.navigate(['/statistics']);
  }
}