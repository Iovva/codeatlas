import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SelectedNodeInfo } from '../graph-canvas/graph-canvas.component';

@Component({
  selector: 'app-node-details-drawer',
  imports: [CommonModule],
  template: `
    <div class="node-details-drawer" [class.open]="selectedNode !== null">
      <div class="drawer-header">
        <h5 class="drawer-title">
          <span class="material-icons me-2">info</span>
          Node Details
        </h5>
        <button type="button" class="btn-close btn-close-white" 
                (click)="closeDrawer()"></button>
      </div>
      
      <div class="drawer-content" *ngIf="selectedNode">
        <!-- Canonical ID -->
        <div class="detail-section">
          <label class="detail-label">Canonical ID</label>
          <div class="detail-value canonical-id">{{ selectedNode.id }}</div>
        </div>
        
        <!-- Display Name -->
        <div class="detail-section">
          <label class="detail-label">Display Name</label>
          <div class="detail-value display-name">{{ selectedNode.label }}</div>
        </div>
        
        <!-- Lines of Code -->
        <div class="detail-section">
          <label class="detail-label">Lines of Code</label>
          <div class="detail-value metric">
            <span class="metric-value">{{ formatNumber(selectedNode.loc) }}</span>
            <span class="metric-unit">LOC</span>
          </div>
        </div>
        
        <!-- Fan-In -->
        <div class="detail-section">
          <label class="detail-label">Fan-In</label>
          <div class="detail-value metric">
            <span class="metric-value">{{ selectedNode.fanIn }}</span>
            <span class="metric-unit">dependencies</span>
          </div>
          <small class="detail-help">Number of nodes that depend on this node</small>
        </div>
        
        <!-- Fan-Out -->
        <div class="detail-section">
          <label class="detail-label">Fan-Out</label>
          <div class="detail-value metric">
            <span class="metric-value">{{ selectedNode.fanOut }}</span>
            <span class="metric-unit">dependencies</span>
          </div>
          <small class="detail-help">Number of nodes this node depends on</small>
        </div>
        
        <!-- Actions -->
        <div class="detail-actions">
          <button type="button" class="btn btn-outline-primary btn-sm w-100 mb-2"
                  (click)="showImpact()">
            <span class="material-icons me-1">trending_up</span>
            Show Impact
          </button>
          
          <button type="button" class="btn btn-outline-secondary btn-sm w-100"
                  (click)="findPaths()">
            <span class="material-icons me-1">route</span>
            Find Paths
          </button>
        </div>
      </div>
      
      <div class="drawer-empty" *ngIf="!selectedNode">
        <div class="empty-state">
          <span class="material-icons">touch_app</span>
          <p>Click on a node to see details</p>
        </div>
      </div>
    </div>
  `,
  styleUrls: ['./node-details-drawer.component.css']
})
export class NodeDetailsDrawerComponent implements OnChanges {
  @Input() selectedNode: SelectedNodeInfo | null = null;
  @Output() closeRequested = new EventEmitter<void>();
  @Output() impactRequested = new EventEmitter<string>();
  @Output() pathsRequested = new EventEmitter<string>();
  
  ngOnChanges(changes: SimpleChanges): void {
    // Component will automatically update when selectedNode changes
  }
  
  closeDrawer(): void {
    this.closeRequested.emit();
  }
  
  showImpact(): void {
    if (this.selectedNode) {
      this.impactRequested.emit(this.selectedNode.id);
    }
  }
  
  findPaths(): void {
    if (this.selectedNode) {
      this.pathsRequested.emit(this.selectedNode.id);
    }
  }
  
  formatNumber(value: number): string {
    if (value >= 1000000) {
      return (value / 1000000).toFixed(1) + 'M';
    } else if (value >= 1000) {
      return (value / 1000).toFixed(1) + 'K';
    } else {
      return value.toString();
    }
  }
}
