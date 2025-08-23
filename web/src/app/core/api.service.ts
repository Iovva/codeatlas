import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface AnalyzeRequest {
  repoUrl: string;
  branch?: string;
}

export interface AnalysisResult {
  meta: {
    repo: string;
    branch?: string;
    commit?: string;
    generatedAt: string;
  };
  graphs: {
    namespace: {
      nodes: Array<{ id: string; label: string; loc: number; fanIn: number; fanOut: number; }>;
      edges: Array<{ from: string; to: string; }>;
    };
    file: {
      nodes: Array<{ id: string; label: string; loc: number; fanIn: number; fanOut: number; }>;
      edges: Array<{ from: string; to: string; }>;
    };
  };
  metrics: {
    counts: {
      namespaceNodes: number;
      fileNodes: number;
      edges: number;
    };
    fanInTop: Array<{ id: string; label: string; loc: number; fanIn: number; fanOut: number; }>;
    fanOutTop: Array<{ id: string; label: string; loc: number; fanIn: number; fanOut: number; }>;
  };
  cycles: Array<{
    id: number;
    size: number;
    sample: string[];
  }>;
}

export interface ApiError {
  code: string;
  message: string;
  detectedLanguages?: string[];
  foundFiles?: string[];
}

export type ApiErrorCode = 
  | 'NoSolutionOrProject'
  | 'MissingSdk'
  | 'BuildFailed'
  | 'CloneFailed'
  | 'Timeout'
  | 'LimitsExceeded';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  analyze(request: AnalyzeRequest): Observable<AnalysisResult> {
    return this.http.post<AnalysisResult>(`${this.baseUrl}/analyze`, request)
      .pipe(
        catchError(this.handleError.bind(this))
      );
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    let apiError: ApiError;

    if (error.error && typeof error.error === 'object' && error.error.code && error.error.message) {
      // Backend returned structured error
      apiError = {
        code: error.error.code,
        message: error.error.message,
        detectedLanguages: error.error.detectedLanguages,
        foundFiles: error.error.foundFiles
      };
    } else if (error.status === 0) {
      // Network error
      apiError = {
        code: 'NetworkError',
        message: 'Unable to connect to the analysis server. Please check your connection and try again.'
      };
    } else {
      // Other HTTP errors
      apiError = {
        code: 'UnknownError',
        message: `Analysis failed with status ${error.status}. Please try again.`
      };
    }

    return throwError(() => apiError);
  }

  getErrorDisplayMessage(error: ApiError): string {
    const errorMessages: Record<string, string> = {
      'NoSolutionOrProject': 'Repository Error',
      'MissingSdk': 'SDK Missing',
      'BuildFailed': 'Build Failed',
      'CloneFailed': 'Clone Failed',
      'Timeout': 'Request Timeout',
      'LimitsExceeded': 'Size Limit Exceeded',
      'NetworkError': 'Connection Error',
      'UnknownError': 'Unknown Error'
    };

    return errorMessages[error.code] || 'Analysis Error';
  }
}
