import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, AnalysisResult, ApiError } from '../../core/api.service';
import { StateService, AppState, Scope } from '../../core/state.service';
import { ToasterService } from '../../core/toaster.service';
import { GraphCanvasComponent, SelectedNodeInfo, FilterState } from '../../shared/graph-canvas/graph-canvas.component';
import { NodeDetailsDrawerComponent } from '../../shared/node-details-drawer/node-details-drawer.component';
import { FilterTreeComponent } from '../../shared/filter-tree/filter-tree.component';

@Component({
  selector: 'app-explorer',
  imports: [CommonModule, FormsModule, GraphCanvasComponent, NodeDetailsDrawerComponent, FilterTreeComponent],
  templateUrl: './explorer.html',
  styleUrl: './explorer.css'
})
export class Explorer implements OnInit, OnDestroy {
  repoUrl: string = '';
  branch: string = '';
  
  // State from StateService
  currentState: AppState;
  
  // Graph interaction state
  selectedNode: SelectedNodeInfo | null = null;
  filterState: FilterState = {
    hiddenNodes: new Set(),
    searchHighlights: new Set(),
    neighborsOnly: false,
    selectedNodeId: null
  };
  
  private destroy$ = new Subject<void>();

  constructor(
    private apiService: ApiService,
    private stateService: StateService,
    private toasterService: ToasterService
  ) {
    this.currentState = this.stateService.currentState;
  }

  ngOnInit(): void {
    // Subscribe to state changes
    this.stateService.state$
      .pipe(takeUntil(this.destroy$))
      .subscribe(state => {
        this.currentState = state;
        console.log('Explorer state updated:', {
          hasAnalysisResult: !!state.analysisResult,
          isAnalyzing: state.isAnalyzing,
          currentScope: state.ui.currentScope,
          namespaceNodes: state.analysisResult?.graphs?.namespace?.nodes?.length,
          fileNodes: state.analysisResult?.graphs?.file?.nodes?.length
        });
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // Convenience getters
  get isAnalyzing(): boolean {
    return this.currentState.isAnalyzing;
  }

  get analysisResult(): AnalysisResult | null {
    return this.currentState.analysisResult;
  }

  get currentScope(): Scope {
    return this.currentState.ui.currentScope;
  }

  get searchTerm(): string {
    return this.currentState.ui.searchTerm;
  }

  get error(): string | null {
    return this.currentState.error;
  }

  analyze(): void {
    if (!this.repoUrl.trim()) {
      this.stateService.setError('Please enter a repository URL');
      return;
    }

    // Clear any previous errors
    this.stateService.clearError();
    this.stateService.setAnalyzing(true);
    
    const request = {
      repoUrl: this.repoUrl.trim(),
      ...(this.branch.trim() && { branch: this.branch.trim() })
    };

    this.apiService.analyze(request).subscribe({
      next: (result) => {
        console.log('Analysis complete:', result);
        this.stateService.setAnalysisResult(result);
      },
      error: (error: ApiError) => {
        console.error('Analysis failed:', error);
        
        // IMPORTANT: Stop the loading state
        this.stateService.setAnalyzing(false);
        
        // Check if this is a repository type error with detected languages
        if (error.code === 'NoSolutionOrProject' && error.detectedLanguages && error.foundFiles) {
          // Show beautiful modal popup for repository type errors
          this.toasterService.showRepositoryError(this.repoUrl, error.detectedLanguages, error.foundFiles);
        } else {
          // Show traditional error bar for other errors
          const displayTitle = this.apiService.getErrorDisplayMessage(error);
          const errorMessage = `${displayTitle}: ${error.message}`;
          this.stateService.setError(errorMessage);
        }
      }
    });
  }

  // UI interaction methods
  onScopeChange(scope: Scope): void {
    this.stateService.setScope(scope);
  }

  onSearchChange(searchTerm: string): void {
    this.stateService.setSearchTerm(searchTerm);
  }

  clearError(): void {
    this.stateService.clearError();
  }

  exportPNG() {
    console.log('Export PNG - will be implemented in Step 15');
    alert('PNG export will be implemented in Step 15');
  }

  exportSVG() {
    console.log('Export SVG - will be implemented in Step 15');
    alert('SVG export will be implemented in Step 15');
  }

  exportJSON() {
    console.log('Export JSON - will be implemented in Step 15');
    if (this.analysisResult) {
      const dataStr = JSON.stringify(this.analysisResult, null, 2);
      const dataBlob = new Blob([dataStr], { type: 'application/json' });
      const url = URL.createObjectURL(dataBlob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `codeatlas-${this.currentScope}-${new Date().toISOString().split('T')[0]}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } else {
      alert('No analysis data to export');
    }
  }

  save() {
    console.log('Save - will be implemented in Step 15');
    if (this.analysisResult) {
      this.exportJSON(); // For now, save is the same as export JSON
    } else {
      this.stateService.setError('No analysis data to save');
    }
  }

  load() {
    console.log('Load - will be implemented in Step 15');
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.json';
    input.onchange = (event: any) => {
      const file = event.target.files[0];
      if (file) {
        const reader = new FileReader();
        reader.onload = (e: any) => {
          try {
            const result = JSON.parse(e.target.result);
            this.stateService.setAnalysisResult(result);
            console.log('Loaded analysis data:', result);
          } catch (error) {
            this.stateService.setError('Invalid JSON file format');
          }
        };
        reader.readAsText(file);
      }
    };
    input.click();
  }

  // Graph interaction methods
  onNodeSelected(nodeInfo: SelectedNodeInfo | null): void {
    this.selectedNode = nodeInfo;
  }

  onFilterStateChanged(newFilterState: FilterState): void {
    this.filterState = newFilterState;
  }

  onNodesVisibilityChanged(hiddenNodes: Set<string>): void {
    this.filterState = { 
      ...this.filterState, 
      hiddenNodes 
    };
  }

  onCloseDrawer(): void {
    this.selectedNode = null;
    this.filterState = { 
      ...this.filterState, 
      selectedNodeId: null,
      neighborsOnly: false
    };
  }

  onImpactRequested(nodeId: string): void {
    console.log('Impact analysis requested for:', nodeId);
    // Will be implemented in Step 14
    alert(`Impact analysis for ${nodeId} will be implemented in Step 14`);
  }

  onPathsRequested(nodeId: string): void {
    console.log('Path analysis requested for:', nodeId);
    // Will be implemented in Step 14
    alert(`Path analysis for ${nodeId} will be implemented in Step 14`);
  }

  toggleNeighborsOnly(): void {
    this.filterState = {
      ...this.filterState,
      neighborsOnly: !this.filterState.neighborsOnly
    };
  }
}
