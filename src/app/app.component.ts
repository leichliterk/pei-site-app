import { Component } from '@angular/core';
import { ElectronService } from './services/electron.service';
import { ConnectionStatusComponent } from './components/connection-status/connection-status.component';

@Component({
  selector: 'app-root',
  imports: [ConnectionStatusComponent],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent {
  selectedFile: any = null;
  isLoading = false;
  error: string | null = null;

  constructor(public electronService: ElectronService) {}

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
}