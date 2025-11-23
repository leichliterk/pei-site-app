import { Component } from '@angular/core';
import { ConnectionStatusComponent } from "../connection-status/connection-status.component";
import { FtpStatusComponent } from "../ftp-status/ftp-status.component";
import { CardModule } from 'primeng/card';

@Component({
  selector: 'app-home',
  imports: [ConnectionStatusComponent, FtpStatusComponent, CardModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {

}
