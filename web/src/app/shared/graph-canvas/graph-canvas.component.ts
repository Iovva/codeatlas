import { Component, OnInit, OnDestroy, ElementRef, ViewChild, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import cytoscape, { Core, EdgeSingular, NodeSingular } from 'cytoscape';
import ELK from 'elkjs/lib/elk.bundled.js';
import { AnalysisResult, GraphData, Node, Edge } from '../../core/api.service';
import { Scope } from '../../core/state.service';

export interface SelectedNodeInfo {
  id: string;
  label: string;
  loc: number;
  fanIn: number;
  fanOut: number;
}

export interface FilterState {
  hiddenNodes: Set<string>;
  searchHighlights: Set<string>;
  neighborsOnly: boolean;
  selectedNodeId: string | null;
}

@Component({
  selector: 'app-graph-canvas',
  imports: [CommonModule],
  template: `
    <div class="graph-canvas-container h-100">
      <div #cytoscapeContainer class="cytoscape-container h-100"></div>
    </div>
  `,
  styleUrls: ['./graph-canvas.component.css']
})
export class GraphCanvasComponent implements OnInit, OnDestroy, OnChanges {
  @ViewChild('cytoscapeContainer', { static: true }) cytoscapeContainer!: ElementRef;
  
  @Input() analysisResult: AnalysisResult | null = null;
  @Input() currentScope: Scope = 'namespace';
  @Input() searchTerm: string = '';
  @Input() filterState: FilterState = {
    hiddenNodes: new Set(),
    searchHighlights: new Set(),
    neighborsOnly: false,
    selectedNodeId: null
  };
  
  @Output() nodeSelected = new EventEmitter<SelectedNodeInfo | null>();
  @Output() filterStateChanged = new EventEmitter<FilterState>();
  
  public cy: Core | null = null;
  private destroy$ = new Subject<void>();
  private layoutSubject = new Subject<void>();
  
  ngOnInit(): void {
    this.initializeCytoscape();
    
    // Set up debounced layout updates
    this.layoutSubject.pipe(
      debounceTime(150),
      distinctUntilChanged()
    ).subscribe(() => {
      this.runLayout();
    });
  }
  
  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    
    if (this.cy) {
      this.cy.destroy();
    }
  }
  
  ngOnChanges(changes: SimpleChanges): void {
    if (changes['analysisResult'] || changes['currentScope']) {
      // Wait for Cytoscape to be ready before updating
      setTimeout(() => this.updateGraph(), 100);
    }
    
    if (changes['searchTerm']) {
      this.updateSearchHighlights();
    }
    
    if (changes['filterState']) {
      this.applyFilters();
    }
  }
  
  private initializeCytoscape(): void {
    console.log('Initializing Cytoscape...', {
      container: this.cytoscapeContainer?.nativeElement,
      containerExists: !!this.cytoscapeContainer?.nativeElement
    });
    
    this.cy = cytoscape({
      container: this.cytoscapeContainer.nativeElement,
      style: [
        {
          selector: 'node',
          style: {
            'background-color': '#4299e1',
            'background-gradient-direction': 'to-bottom-right',
            'background-gradient-stop-colors': ['#4299e1', '#2b77cb'],
            'border-color': '#63b3ed',
            'border-width': 2,
            'label': 'data(label)',
            'color': '#ffffff',
            'text-valign': 'center',
            'text-halign': 'center',
            'font-size': '14px',
            'font-family': 'Inter, sans-serif',
            'font-weight': 600,
            'width': 80,
            'height': 60,
            'shape': 'roundrectangle',
            'text-outline-color': '#1a202c',
            'text-outline-width': 1
          }
        },
        {
          selector: 'node:selected',
          style: {
            'background-color': '#3182ce',
            'border-color': '#63b3ed',
            'border-width': 3
          }
        },
        {
          selector: 'node.highlighted',
          style: {
            'background-color': '#d69e2e',
            'border-color': '#f6e05e',
            'border-width': 3
          }
        },
        {
          selector: 'node.neighbor',
          style: {
            'background-color': '#38a169',
            'border-color': '#68d391'
          }
        },
        {
          selector: 'node.hidden',
          style: {
            'display': 'none'
          }
        },
        {
          selector: 'edge',
          style: {
            'width': 3,
            'line-color': '#718096',
            'target-arrow-color': '#718096',
            'target-arrow-shape': 'triangle',
            'arrow-scale': 1.2,
            'curve-style': 'bezier',
            'opacity': 0.8
          }
        },
        {
          selector: 'edge.highlighted',
          style: {
            'line-color': '#3182ce',
            'target-arrow-color': '#3182ce',
            'width': 3
          }
        },
        {
          selector: 'edge.hidden',
          style: {
            'display': 'none'
          }
        }
      ],
      layout: { name: 'preset' },
      minZoom: 0.1,
      maxZoom: 5,
      wheelSensitivity: 0.8
    });
    
    // Set up event handlers
    this.cy.on('tap', 'node', (event) => {
      const node = event.target;
      this.selectNode(node);
    });
    
    this.cy.on('tap', (event) => {
      if (event.target === this.cy) {
        // Clicked on empty canvas
        this.selectNode(null);
      }
    });
  }
  
  private updateGraph(): void {
    console.log('updateGraph called', {
      cy: !!this.cy,
      analysisResult: !!this.analysisResult,
      currentScope: this.currentScope
    });
    
    if (!this.cy || !this.analysisResult) return;
    
    const graphData = this.getCurrentGraphData();
    console.log('Graph data:', {
      graphData,
      nodeCount: graphData?.nodes?.length,
      edgeCount: graphData?.edges?.length
    });
    
    if (!graphData) return;
    
    // Clear existing elements
    this.cy.elements().remove();
    
    // Add nodes
    const nodes = graphData.nodes.map((node: Node) => ({
      data: {
        id: node.id,
        label: node.label,
        loc: node.loc,
        fanIn: node.fanIn,
        fanOut: node.fanOut
      }
    }));
    
    // Add edges
    const edges = graphData.edges.map((edge: Edge) => ({
      data: {
        id: `${edge.from}-${edge.to}`,
        source: edge.from,
        target: edge.to
      }
    }));
    
    this.cy.add([...nodes, ...edges]);
    
    // Trigger layout update
    this.layoutSubject.next();
  }
  
  private getCurrentGraphData(): GraphData | null {
    if (!this.analysisResult) return null;
    
    return this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace 
      : this.analysisResult.graphs.file;
  }
  
  private async runLayout(): Promise<void> {
    if (!this.cy) return;
    
    const nodes = this.cy.nodes().not('.hidden');
    const edges = this.cy.edges().not('.hidden');
    
    if (nodes.length === 0) return;
    
    // Prepare data for ELK
    const elkNodes = nodes.map(node => ({
      id: node.id(),
      width: 80,
      height: 50
    }));
    
    const elkEdges = edges.map(edge => ({
      id: edge.id(),
      sources: [(edge as EdgeSingular).source().id()],
      targets: [(edge as EdgeSingular).target().id()]
    }));
    
    const elkGraph = {
      id: 'root',
      children: elkNodes,
      edges: elkEdges,
      layoutOptions: {
        'elk.algorithm': 'layered',
        'elk.direction': 'DOWN',
        'elk.spacing.nodeNode': '50',
        'elk.layered.spacing.nodeNodeBetweenLayers': '70',
        'elk.spacing.edgeNode': '30'
      }
    };
    
    try {
      const elk = new ELK();
      const elkResult = await elk.layout(elkGraph);
      
      // Apply positions
      if (elkResult.children) {
        elkResult.children.forEach((child: any) => {
          const node = this.cy!.getElementById(child.id);
          if (node.length > 0 && child.x !== undefined && child.y !== undefined) {
            node.position({ x: child.x + (child.width || 80) / 2, y: child.y + (child.height || 50) / 2 });
          }
        });
      }
      
      // Fit the viewport
      this.cy.fit(undefined, 50);
    } catch (error) {
      console.error('Layout failed:', error);
      // Fallback to simple grid layout
      this.cy.layout({ name: 'grid', fit: true, padding: 50 }).run();
    }
  }
  
  private selectNode(node: NodeSingular | null): void {
    if (!this.cy) return;
    
    // Clear previous selection
    this.cy.nodes().removeClass('selected');
    this.cy.edges().removeClass('highlighted');
    
    if (node) {
      node.addClass('selected');
      
      const nodeInfo: SelectedNodeInfo = {
        id: node.data('id'),
        label: node.data('label'),
        loc: node.data('loc') || 0,
        fanIn: node.data('fanIn') || 0,
        fanOut: node.data('fanOut') || 0
      };
      
      this.nodeSelected.emit(nodeInfo);
      
      // Update filter state
      const newFilterState = { 
        ...this.filterState, 
        selectedNodeId: node.id() 
      };
      this.filterState = newFilterState;
      this.filterStateChanged.emit(newFilterState);
      
      // Apply neighbors-only if enabled
      if (this.filterState.neighborsOnly) {
        this.applyNeighborsOnlyFilter(node);
      }
    } else {
      this.nodeSelected.emit(null);
      
      // Clear selection from filter state
      const newFilterState = { 
        ...this.filterState, 
        selectedNodeId: null 
      };
      this.filterState = newFilterState;
      this.filterStateChanged.emit(newFilterState);
      
      // Clear neighbors-only filter
      if (this.filterState.neighborsOnly) {
        this.clearNeighborsOnlyFilter();
      }
    }
  }
  
  private updateSearchHighlights(): void {
    if (!this.cy) return;
    
    // Clear previous highlights
    this.cy.nodes().removeClass('highlighted');
    
    if (this.searchTerm.trim()) {
      const searchLower = this.searchTerm.toLowerCase();
      const matchingNodes = this.cy.nodes().filter(node => {
        const id = node.data('id').toLowerCase();
        const label = node.data('label').toLowerCase();
        return id.includes(searchLower) || label.includes(searchLower);
      });
      
      matchingNodes.addClass('highlighted');
      
      // Update search highlights in filter state
      const highlights = new Set(matchingNodes.map(node => node.id()));
      const newFilterState = { 
        ...this.filterState, 
        searchHighlights: highlights 
      };
      this.filterState = newFilterState;
      this.filterStateChanged.emit(newFilterState);
    }
  }
  
  private applyFilters(): void {
    if (!this.cy) return;
    
    // Apply hidden nodes filter
    this.cy.nodes().removeClass('hidden');
    this.cy.edges().removeClass('hidden');
    
    this.filterState.hiddenNodes.forEach(nodeId => {
      const node = this.cy!.getElementById(nodeId);
      node.addClass('hidden');
      
      // Hide connected edges
      node.connectedEdges().addClass('hidden');
    });
    
    // Apply neighbors-only filter if enabled and node selected
    if (this.filterState.neighborsOnly && this.filterState.selectedNodeId) {
      const selectedNode = this.cy.getElementById(this.filterState.selectedNodeId);
      if (selectedNode.length > 0) {
        this.applyNeighborsOnlyFilter(selectedNode);
      }
    }
    
    // Trigger layout update after filter changes
    this.layoutSubject.next();
  }
  
  private applyNeighborsOnlyFilter(selectedNode: NodeSingular): void {
    if (!this.cy) return;
    
    // Hide all nodes except selected and neighbors
    const neighbors = selectedNode.neighborhood().nodes();
    const visibleNodes = selectedNode.union(neighbors);
    
    this.cy.nodes().not(visibleNodes).addClass('hidden');
    
    // Show only edges between visible nodes
    this.cy.edges().addClass('hidden');
    visibleNodes.connectedEdges().forEach(edge => {
      const source = edge.source();
      const target = edge.target();
      if (visibleNodes.contains(source) && visibleNodes.contains(target)) {
        edge.removeClass('hidden');
      }
    });
  }
  
  private clearNeighborsOnlyFilter(): void {
    if (!this.cy) return;
    
    // Show all nodes that aren't explicitly hidden by other filters
    this.cy.nodes().forEach(node => {
      if (!this.filterState.hiddenNodes.has(node.id())) {
        node.removeClass('hidden');
      }
    });
    
    // Show all edges between visible nodes
    this.cy.edges().removeClass('hidden');
    this.cy.nodes('.hidden').connectedEdges().addClass('hidden');
  }
  
  // Public methods for external control
  toggleNeighborsOnly(): void {
    const newFilterState = {
      ...this.filterState,
      neighborsOnly: !this.filterState.neighborsOnly
    };
    this.filterState = newFilterState;
    this.filterStateChanged.emit(newFilterState);
    this.applyFilters();
  }
  
  exportPNG(): string | null {
    if (!this.cy) return null;
    return this.cy.png({ scale: 2, full: true });
  }
  
  exportSVG(): string | null {
    if (!this.cy) return null;
    // SVG export not available in this Cytoscape version
    // Will be implemented in Step 15 with proper export functionality
    return null;
  }
}
