import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { AnalysisResult } from './api.service';

export type Scope = 'namespace' | 'file';

export interface UiState {
  currentScope: Scope;
  selectedNodeId: string | null;
  searchTerm: string;
  activeFilters: string[];
  isNeighborsOnly: boolean;
  isImpactMode: boolean;
  pathModeSource: string | null;
  pathModeTarget: string | null;
}

export interface AppState {
  analysisResult: AnalysisResult | null;
  isAnalyzing: boolean;
  error: string | null;
  ui: UiState;
}

const initialUiState: UiState = {
  currentScope: 'namespace',
  selectedNodeId: null,
  searchTerm: '',
  activeFilters: [],
  isNeighborsOnly: false,
  isImpactMode: false,
  pathModeSource: null,
  pathModeTarget: null
};

const initialState: AppState = {
  analysisResult: null,
  isAnalyzing: false,
  error: null,
  ui: initialUiState
};

@Injectable({
  providedIn: 'root'
})
export class StateService {
  private stateSubject = new BehaviorSubject<AppState>(initialState);
  
  public state$ = this.stateSubject.asObservable();

  get currentState(): AppState {
    return this.stateSubject.value;
  }

  // Analysis state methods
  setAnalyzing(isAnalyzing: boolean): void {
    this.updateState({ isAnalyzing });
  }

  setAnalysisResult(result: AnalysisResult | null): void {
    this.updateState({ 
      analysisResult: result,
      error: null,
      isAnalyzing: false 
    });
  }

  setError(error: string | null): void {
    this.updateState({ 
      error,
      isAnalyzing: false 
    });
  }

  clearError(): void {
    this.updateState({ error: null });
  }

  // UI state methods
  setScope(scope: Scope): void {
    this.updateUiState({ 
      currentScope: scope,
      selectedNodeId: null // Clear selection when switching scope
    });
  }

  setSelectedNode(nodeId: string | null): void {
    this.updateUiState({ selectedNodeId: nodeId });
  }

  setSearchTerm(searchTerm: string): void {
    this.updateUiState({ searchTerm });
  }

  setActiveFilters(filters: string[]): void {
    this.updateUiState({ activeFilters: filters });
  }

  toggleNeighborsOnly(): void {
    const current = this.currentState.ui.isNeighborsOnly;
    this.updateUiState({ isNeighborsOnly: !current });
  }

  toggleImpactMode(): void {
    const current = this.currentState.ui.isImpactMode;
    this.updateUiState({ 
      isImpactMode: !current,
      // Clear path mode when toggling impact
      pathModeSource: !current ? null : this.currentState.ui.pathModeSource,
      pathModeTarget: !current ? null : this.currentState.ui.pathModeTarget
    });
  }

  setPathMode(source: string | null, target: string | null): void {
    this.updateUiState({ 
      pathModeSource: source,
      pathModeTarget: target,
      // Clear impact mode when setting path mode
      isImpactMode: (source || target) ? false : this.currentState.ui.isImpactMode
    });
  }

  resetUiState(): void {
    this.updateUiState(initialUiState);
  }

  // Helper methods
  private updateState(partial: Partial<AppState>): void {
    const currentState = this.currentState;
    const newState = { ...currentState, ...partial };
    this.stateSubject.next(newState);
  }

  private updateUiState(partial: Partial<UiState>): void {
    const currentState = this.currentState;
    const newUiState = { ...currentState.ui, ...partial };
    this.updateState({ ui: newUiState });
  }

  // Computed getters for convenience
  getCurrentGraph(): { nodes: any[], edges: any[] } | null {
    const result = this.currentState.analysisResult;
    if (!result) return null;
    
    const scope = this.currentState.ui.currentScope;
    return scope === 'namespace' ? result.graphs.namespace : result.graphs.file;
  }

  getSelectedNode(): any | null {
    const graph = this.getCurrentGraph();
    const selectedId = this.currentState.ui.selectedNodeId;
    
    if (!graph || !selectedId) return null;
    
    return graph.nodes.find(node => node.id === selectedId) || null;
  }
}
