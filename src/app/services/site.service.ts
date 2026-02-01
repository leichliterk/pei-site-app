import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface UptimeSession {
  connected_at: string;
  disconnected_at: string;
  duration_ms: number;
  disconnect_reason: string;
}

export interface UptimeResponse {
  tenant_id: number;
  site_id: number;
  days: number;
  start_date: string;
  end_date: string;
  total_time_ms: number;
  total_uptime_ms: number;
  uptime_percentage: number;
  sessions: UptimeSession[];
}

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

  /**
   * Get connection uptime statistics for a site
   * @param tenantId - The tenant ID
   * @param siteId - The site ID
   * @param days - Number of days to look back (optional, default: 7)
   * @returns Observable of the uptime response
   */
  getUptime(tenantId: number, siteId: number, days?: number): Observable<UptimeResponse> {
    const url = `${this.apiUrl}/site/uptime/${tenantId}/${siteId}`;
    let params = new HttpParams();
    if (days !== undefined) {
      params = params.set('days', days.toString());
    }
    return this.http.get<UptimeResponse>(url, { params });
  }
}
