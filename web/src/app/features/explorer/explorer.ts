import { Component, OnInit, OnDestroy, ViewChild } from '@angular/core';
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
  @ViewChild(GraphCanvasComponent) graphCanvas!: GraphCanvasComponent;
  
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
  
  // Resizable filter panel state
  filterPanelWidth: number = 300; // Default width
  private isResizing: boolean = false;
  private startX: number = 0;
  private startWidth: number = 0;
  
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
    
    // Clean up resize event listeners
    document.removeEventListener('mousemove', this.handleResize.bind(this));
    document.removeEventListener('mouseup', this.stopResize.bind(this));
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
    
    // Update filter state to keep selectedNodeId in sync
    this.filterState = {
      ...this.filterState,
      selectedNodeId: nodeInfo?.id || null
    };
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

  onFilterTreeNodeSelected(nodeId: string): void {
    console.log(`🎯 Filter tree node selected: ${nodeId}`);
    
    if (!this.analysisResult) return;
    
    // Find the node in the current scope's graph data
    const nodes = this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace.nodes 
      : this.analysisResult.graphs.file.nodes;
    
    const node = nodes.find(n => n.id === nodeId);
    
    if (node) {
      // Convert to SelectedNodeInfo format
      const selectedNodeInfo: SelectedNodeInfo = {
        id: node.id,
        label: node.label,
        loc: node.loc,
        fanIn: node.fanIn,
        fanOut: node.fanOut
      };
      
      console.log(`🎯 Opening right drawer for: ${selectedNodeInfo.label}`);
      
      // Update filter state to include the selected node ID (for graph canvas)
      this.filterState = {
        ...this.filterState,
        selectedNodeId: node.id
      };
      
      // Center the graph on the selected node (only for filter tree selections)
      if (this.graphCanvas) {
        this.graphCanvas.centerOnNode(node.id);
      }
      
      // Open the right drawer
      this.onNodeSelected(selectedNodeInfo);
    } else {
      console.warn(`🎯 Node not found in ${this.currentScope} scope: ${nodeId}`);
    }
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
  
  onNeighborsOnlyToggled(enabled: boolean): void {
    this.filterState = {
      ...this.filterState,
      neighborsOnly: enabled
    };
  }
  
  getNodeCount(): number {
    if (!this.analysisResult) return 0;
    const graphData = this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace 
      : this.analysisResult.graphs.file;
    return graphData?.nodes?.length || 0;
  }
  
  getEdgeCount(): number {
    if (!this.analysisResult) return 0;
    const graphData = this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace 
      : this.analysisResult.graphs.file;
    return graphData?.edges?.length || 0;
  }

  // Resizable filter panel methods
  startResize(event: MouseEvent): void {
    event.preventDefault();
    this.isResizing = true;
    this.startX = event.clientX;
    this.startWidth = this.filterPanelWidth;
    
    // Add global event listeners
    document.addEventListener('mousemove', this.handleResize.bind(this));
    document.addEventListener('mouseup', this.stopResize.bind(this));
    
    // Add visual feedback
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }

  private handleResize(event: MouseEvent): void {
    if (!this.isResizing) return;
    
    const deltaX = event.clientX - this.startX;
    const newWidth = this.startWidth + deltaX;
    
    // Constrain width between 200px and 600px
    this.filterPanelWidth = Math.max(200, Math.min(600, newWidth));
  }

  private stopResize(): void {
    this.isResizing = false;
    
    // Remove global event listeners
    document.removeEventListener('mousemove', this.handleResize.bind(this));
    document.removeEventListener('mouseup', this.stopResize.bind(this));
    
    // Remove visual feedback
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  }
}
