import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  AgentChangeResponse,
  AgentDenialsSummary,
  AgentView,
  RegisterAgentRequest,
} from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';
import type { DashboardPeriod } from './ordering.service';

/** How far the shop trusts an agent it registered. The server owns the rules; this is the vocabulary. */
export type AgentTrust = 'Known' | 'Verified';

/**
 * The only place in the backoffice that knows the Accounts endpoints: the agent
 * registry (P11-2) and what the shop refused agents (P11-6, P11-7).
 */
@Injectable({ providedIn: 'root' })
export class AccountsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  agents(): Observable<AgentView[]> {
    return this.http.get<AgentView[]>(`${this.baseUrl}/api/agents`);
  }

  register(request: RegisterAgentRequest): Observable<AgentChangeResponse> {
    return this.http.post<AgentChangeResponse>(`${this.baseUrl}/api/agents`, request);
  }

  trust(agentId: string, trust: AgentTrust): Observable<AgentChangeResponse> {
    return this.http.post<AgentChangeResponse>(`${this.baseUrl}/api/agents/${encodeURIComponent(agentId)}/trust`, { trust });
  }

  suspend(agentId: string, reason: string): Observable<AgentChangeResponse> {
    return this.http.post<AgentChangeResponse>(`${this.baseUrl}/api/agents/${encodeURIComponent(agentId)}/suspend`, { reason });
  }

  reinstate(agentId: string): Observable<AgentChangeResponse> {
    return this.http.post<AgentChangeResponse>(`${this.baseUrl}/api/agents/${encodeURIComponent(agentId)}/reinstate`, {});
  }

  agentDenials(period: DashboardPeriod): Observable<AgentDenialsSummary> {
    return this.http.get<AgentDenialsSummary>(`${this.baseUrl}/api/dashboard/agent-denials`, {
      params: new HttpParams().set('period', period),
    });
  }
}
