import { Component, OnInit, OnDestroy } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { Subject, takeUntil } from 'rxjs';
import { ElectronService } from './services/electron.service';
import { FtpSyncService } from './services/ftp-sync.service';
import { WebSocketService } from './services/websocket.service';
import { LocalServiceService } from './services/local-service.service';
import { environment } from '../environments/environment';
import { ButtonModule } from 'primeng/button';
import { ToolbarModule } from 'primeng/toolbar';
import { TooltipModule } from 'primeng/tooltip';
import { MessageModule } from 'primeng/message';

@Component({
  selector: 'app-root',
  imports: [CommonModule, RouterOutlet, ButtonModule, ToolbarModule, TooltipModule, MessageModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent implements OnInit, OnDestroy {
  selectedFile: any = null;
  isLoading = false;
  error: string | null = null;
  environment = environment;
  private destroy$ = new Subject<void>();

  constructor(
    public electronService: ElectronService,
    private router: Router,
    private ftpSyncService: FtpSyncService,
    private webSocketService: WebSocketService,
    private localServiceService: LocalServiceService,
    private titleService: Title
  ) {}

  ngOnInit(): void {
    // Set window title from environment
    this.titleService.setTitle(environment.appName || 'PEI Site App');

    // Check if local background service is available
    // Only connect directly to WebSocket if the service is NOT running
    // This prevents duplicate connections which cause constant reconnection cycles
    this.localServiceService.checkHealth()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          console.log('Background service is running - UI will use service for status');
          // Don't connect WebSocket - the service handles the connection
        },
        error: () => {
          console.log('Background service not available - connecting WebSocket directly');
          this.webSocketService.connect();
        }
      });

    // Listen for navigation events from system tray menu
    this.electronService.onNavigate((route: string) => {
      this.router.navigate([route]);
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
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
}