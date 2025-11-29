import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class SiteService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  /**
   * Update the site name for a given tenant and site
   * @param tenantId - The tenant ID
   * @param siteId - The site ID
   * @param siteName - The new site name
   * @returns Observable of the API response
   */
  updateSiteName(tenantId: string, siteId: string, siteName: string): Observable<any> {
    const url = `${this.apiUrl}/site/updateSiteName/${tenantId}/${siteId}`;
    return this.http.put(url, { siteName });
  }

  /**
   * Update the site ID for a given tenant
   * @param tenantId - The tenant ID
   * @param oldSiteId - The current site ID
   * @param newSiteId - The new site ID
   * @returns Observable of the API response
   */
  updateSiteId(tenantId: string, oldSiteId: string, newSiteId: string): Observable<any> {
    const url = `${this.apiUrl}/site/updateSiteId/${tenantId}/${oldSiteId}`;
    return this.http.post(url, { newSiteId });
  }
}
