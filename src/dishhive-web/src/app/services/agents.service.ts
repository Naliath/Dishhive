import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * Client for the AI agents API. All endpoints degrade gracefully when AI is disabled
 * server-side (status returns `available: false`; mutating endpoints return HTTP 503).
 */
export interface AgentStatus {
  available: boolean;
  provider: string;
  model: string;
}

export interface LearnedRecipeSource {
  host: string;
  providerKey: string;
  strategy: 'JsonLd' | 'XPath';
  learnedAt: string;
  lastUsedAt: string | null;
  useCount: number;
  sourceUrl: string;
}

export interface MealSuggestionRequest {
  date: string;       // ISO date (yyyy-MM-dd)
  mealType: string;
  vagueIntent?: string;
  intentTag?: string;
  attendingFamilyMemberIds: string[];
}

export interface MealSuggestion {
  recipeId: string | null;
  dishLabel: string;
  reason: string;
}

export interface ChatTurn {
  role: 'user' | 'assistant' | 'system';
  content: string;
}

export interface ChatReply {
  reply: string;
}

@Injectable({ providedIn: 'root' })
export class AgentsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/agents';

  status(): Observable<AgentStatus> {
    return this.http.get<AgentStatus>(`${this.base}/status`);
  }

  suggest(req: MealSuggestionRequest): Observable<MealSuggestion> {
    return this.http.post<MealSuggestion>(`${this.base}/meal-planning/suggest`, req);
  }

  chat(messages: ChatTurn[]): Observable<ChatReply> {
    return this.http.post<ChatReply>(`${this.base}/meal-planning/chat`, { messages });
  }

  learnedSources(): Observable<LearnedRecipeSource[]> {
    return this.http.get<LearnedRecipeSource[]>(`${this.base}/learned-sources`);
  }

  deleteLearnedSource(host: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/learned-sources/${encodeURIComponent(host)}`);
  }
}
