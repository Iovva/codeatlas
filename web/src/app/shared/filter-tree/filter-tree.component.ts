import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnalysisResult, Node } from '../../core/api.service';
import { Scope } from '../../core/state.service';

export interface TreeNode {
  id: string;
  label: string;
  children: TreeNode[];
  isExpanded: boolean;
  isChecked: boolean;
  isIndeterminate: boolean;
  nodeIds: Set<string>; // Node IDs that belong to this tree node
}

@Component({
  selector: 'app-tree-node',
  imports: [CommonModule],
  template: `
    <div class="tree-node" [style.margin-left.px]="level * 20">
      <div class="tree-node-content">
        <!-- Expand/Collapse Button -->
        <button type="button" 
                class="tree-expand-btn"
                [class.invisible]="node.children.length === 0"
                (click)="toggleNode()">
          <span class="material-icons">
            {{ node.isExpanded ? 'expand_more' : 'chevron_right' }}
          </span>
        </button>
        
        <!-- Checkbox -->
        <div class="form-check">
          <input class="form-check-input" 
                 type="checkbox" 
                 [checked]="node.isChecked"
                 [indeterminate]="node.isIndeterminate"
                 (change)="onCheckboxChange($event)"
                 [id]="'checkbox-' + node.id">
          <label class="form-check-label" [for]="'checkbox-' + node.id">
            <span class="material-icons me-1">
              {{ getNodeIcon() }}
            </span>
            {{ node.label }}
            <span class="node-count" *ngIf="node.nodeIds.size > 1">
              ({{ node.nodeIds.size }})
            </span>
          </label>
        </div>
      </div>
      
      <!-- Children -->
      <div class="tree-children" *ngIf="node.isExpanded && node.children.length > 0">
        <app-tree-node 
          *ngFor="let child of node.children"
          [node]="child"
          [level]="level + 1"
          (nodeToggled)="nodeToggled.emit($event)"
          (nodeChecked)="nodeChecked.emit($event)">
        </app-tree-node>
      </div>
    </div>
  `,
  styles: [`
    .tree-node-content {
      display: flex;
      align-items: center;
      padding: 0.25rem 0;
      border-radius: 4px;
    }
    
    .tree-node-content:hover {
      background-color: rgba(255, 255, 255, 0.05);
    }
    
    .tree-expand-btn {
      background: none;
      border: none;
      color: #a0aec0;
      padding: 0;
      width: 24px;
      height: 24px;
      display: flex;
      align-items: center;
      justify-content: center;
      margin-right: 0.25rem;
    }
    
    .tree-expand-btn:hover {
      color: #e2e8f0;
    }
    
    .tree-expand-btn .material-icons {
      font-size: 1.2rem;
    }
    
    .form-check {
      display: flex;
      align-items: center;
      margin: 0;
    }
    
    .form-check-label {
      display: flex;
      align-items: center;
      color: #e2e8f0;
      font-size: 0.875rem;
      margin: 0;
      cursor: pointer;
    }
    
    .form-check-label .material-icons {
      font-size: 1rem;
    }
    
    .node-count {
      color: #a0aec0;
      font-size: 0.75rem;
      margin-left: 0.25rem;
    }
    
    .tree-children {
      margin-left: 0.5rem;
    }
  `]
})
export class TreeNodeComponent {
  @Input() node!: TreeNode;
  @Input() level: number = 0;
  
  @Output() nodeToggled = new EventEmitter<TreeNode>();
  @Output() nodeChecked = new EventEmitter<{ node: TreeNode, checked: boolean }>();
  
  toggleNode(): void {
    this.nodeToggled.emit(this.node);
  }
  
  onCheckboxChange(event: any): void {
    this.nodeChecked.emit({ node: this.node, checked: event.target.checked });
  }
  
  getNodeIcon(): string {
    if (this.node.id.startsWith('Namespace:')) {
      return this.node.children.length > 0 ? 'account_tree' : 'code';
    } else if (this.node.id.startsWith('Folder:')) {
      return 'folder';
    } else {
      return 'description';
    }
  }
}

@Component({
  selector: 'app-filter-tree',
  imports: [CommonModule, TreeNodeComponent],
  template: `
    <div class="filter-tree">
      <div class="tree-header">
        <h6 class="tree-title">
          <span class="material-icons me-2">
            {{ currentScope === 'namespace' ? 'account_tree' : 'folder' }}
          </span>
          {{ currentScope === 'namespace' ? 'Namespaces' : 'Folders' }}
        </h6>
        <div class="tree-actions">
          <button type="button" class="btn btn-sm btn-outline-secondary"
                  (click)="expandAll()" title="Expand All">
            <span class="material-icons">unfold_more</span>
          </button>
          <button type="button" class="btn btn-sm btn-outline-secondary"
                  (click)="collapseAll()" title="Collapse All">
            <span class="material-icons">unfold_less</span>
          </button>
        </div>
      </div>
      
      <div class="tree-content">
        <div class="tree-stats">
          <small class="text-muted">
            {{ getVisibleNodeCount() }} of {{ getTotalNodeCount() }} visible
          </small>
        </div>
        
        <div class="tree-nodes" *ngIf="rootNodes.length > 0">
          <div *ngFor="let node of rootNodes" class="tree-node-container">
            <app-tree-node 
              [node]="node"
              [level]="0"
              (nodeToggled)="onNodeToggled($event)"
              (nodeChecked)="onNodeChecked($event)">
            </app-tree-node>
          </div>
        </div>
        
        <div class="empty-state" *ngIf="rootNodes.length === 0">
          <span class="material-icons">hourglass_empty</span>
          <p>No data available</p>
        </div>
      </div>
    </div>
  `,
  styleUrls: ['./filter-tree.component.css']
})
export class FilterTreeComponent implements OnChanges {
  @Input() analysisResult: AnalysisResult | null = null;
  @Input() currentScope: Scope = 'namespace';
  @Input() hiddenNodes: Set<string> = new Set();
  
  @Output() nodesVisibilityChanged = new EventEmitter<Set<string>>();
  
  rootNodes: TreeNode[] = [];
  
  ngOnChanges(changes: SimpleChanges): void {
    if (changes['analysisResult'] || changes['currentScope']) {
      this.buildTree();
    }
    
    if (changes['hiddenNodes']) {
      this.updateCheckStates();
    }
  }
  
  private buildTree(): void {
    this.rootNodes = [];
    
    if (!this.analysisResult) return;
    
    const nodes = this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace.nodes 
      : this.analysisResult.graphs.file.nodes;
    
    if (this.currentScope === 'namespace') {
      this.buildNamespaceTree(nodes);
    } else {
      this.buildFileTree(nodes);
    }
    
    this.updateCheckStates();
  }
  
  private buildNamespaceTree(nodes: Node[]): void {
    const namespaceMap = new Map<string, TreeNode>();
    
    // Create tree nodes for each namespace
    nodes.forEach(node => {
      if (!node.id.startsWith('Namespace:')) return;
      
      const namespaceName = node.id.substring('Namespace:'.length);
      const parts = namespaceName.split('.');
      let currentPath = '';
      
      parts.forEach((part: string, index: number) => {
        const parentPath = currentPath;
        currentPath = currentPath ? `${currentPath}.${part}` : part;
        const fullId = `Namespace:${currentPath}`;
        
        if (!namespaceMap.has(fullId)) {
          const treeNode: TreeNode = {
            id: fullId,
            label: part,
            children: [],
            isExpanded: index < 2, // Expand first 2 levels by default
            isChecked: true,
            isIndeterminate: false,
            nodeIds: new Set()
          };
          
          // Add to parent if exists
          if (parentPath) {
            const parentId = `Namespace:${parentPath}`;
            const parent = namespaceMap.get(parentId);
            if (parent) {
              parent.children.push(treeNode);
            }
          } else {
            this.rootNodes.push(treeNode);
          }
          
          namespaceMap.set(fullId, treeNode);
        }
        
        // Add the actual node ID to the final namespace
        if (index === parts.length - 1) {
          const treeNode = namespaceMap.get(fullId)!;
          treeNode.nodeIds.add(node.id);
        }
      });
    });
    
    // Sort children alphabetically
    this.sortTreeNodes(this.rootNodes);
  }
  
  private buildFileTree(nodes: Node[]): void {
    const folderMap = new Map<string, TreeNode>();
    
    nodes.forEach(node => {
      if (!node.id.startsWith('File:')) return;
      
      const filePath = node.id.substring('File:'.length);
      const parts = filePath.split('/').filter((part: string) => part.length > 0);
      
      if (parts.length === 0) return;
      
      let currentPath = '';
      
      // Process all folder parts (except the last one which is the file)
      for (let i = 0; i < parts.length - 1; i++) {
        const part = parts[i];
        const parentPath = currentPath;
        currentPath = currentPath ? `${currentPath}/${part}` : part;
        const folderId = `Folder:${currentPath}`;
        
        if (!folderMap.has(folderId)) {
          const treeNode: TreeNode = {
            id: folderId,
            label: part,
            children: [],
            isExpanded: i < 2, // Expand first 2 levels by default
            isChecked: true,
            isIndeterminate: false,
            nodeIds: new Set()
          };
          
          if (parentPath) {
            const parentId = `Folder:${parentPath}`;
            const parent = folderMap.get(parentId);
            if (parent) {
              parent.children.push(treeNode);
            }
          } else {
            this.rootNodes.push(treeNode);
          }
          
          folderMap.set(folderId, treeNode);
        }
      }
      
      // Add file node to its parent folder
      const fileName = parts[parts.length - 1];
      const parentFolderPath = parts.slice(0, -1).join('/');
      const parentFolderId = parentFolderPath ? `Folder:${parentFolderPath}` : null;
      
      const fileTreeNode: TreeNode = {
        id: node.id,
        label: fileName,
        children: [],
        isExpanded: false,
        isChecked: true,
        isIndeterminate: false,
        nodeIds: new Set([node.id])
      };
      
      if (parentFolderId && folderMap.has(parentFolderId)) {
        const parent = folderMap.get(parentFolderId)!;
        parent.children.push(fileTreeNode);
        parent.nodeIds.add(node.id);
      } else {
        // File at root level
        this.rootNodes.push(fileTreeNode);
      }
    });
    
    // Propagate node IDs up the tree
    this.propagateNodeIds(this.rootNodes);
    
    // Sort children alphabetically
    this.sortTreeNodes(this.rootNodes);
  }
  
  private propagateNodeIds(nodes: TreeNode[]): void {
    nodes.forEach(node => {
      if (node.children.length > 0) {
        this.propagateNodeIds(node.children);
        
        // Add all child node IDs to this node
        node.children.forEach(child => {
          child.nodeIds.forEach(nodeId => {
            node.nodeIds.add(nodeId);
          });
        });
      }
    });
  }
  
  private sortTreeNodes(nodes: TreeNode[]): void {
    nodes.sort((a, b) => a.label.localeCompare(b.label));
    nodes.forEach(node => {
      if (node.children.length > 0) {
        this.sortTreeNodes(node.children);
      }
    });
  }
  
  private updateCheckStates(): void {
    this.updateNodeCheckStates(this.rootNodes);
  }
  
  private updateNodeCheckStates(nodes: TreeNode[]): void {
    nodes.forEach(node => {
      if (node.children.length > 0) {
        this.updateNodeCheckStates(node.children);
        
        // Update parent state based on children
        const checkedChildren = node.children.filter(child => child.isChecked);
        const indeterminateChildren = node.children.filter(child => child.isIndeterminate);
        
        if (checkedChildren.length === node.children.length) {
          node.isChecked = true;
          node.isIndeterminate = false;
        } else if (checkedChildren.length === 0 && indeterminateChildren.length === 0) {
          node.isChecked = false;
          node.isIndeterminate = false;
        } else {
          node.isChecked = false;
          node.isIndeterminate = true;
        }
      } else {
        // Leaf node - check if any of its node IDs are hidden
        const hiddenCount = Array.from(node.nodeIds).filter(id => this.hiddenNodes.has(id)).length;
        
        if (hiddenCount === 0) {
          node.isChecked = true;
          node.isIndeterminate = false;
        } else if (hiddenCount === node.nodeIds.size) {
          node.isChecked = false;
          node.isIndeterminate = false;
        } else {
          node.isChecked = false;
          node.isIndeterminate = true;
        }
      }
    });
  }
  
  onNodeToggled(node: TreeNode): void {
    node.isExpanded = !node.isExpanded;
  }
  
  onNodeChecked(event: { node: TreeNode, checked: boolean }): void {
    const { node, checked } = event;
    
    // Update the node and all its children
    this.setNodeChecked(node, checked);
    
    // Create new hidden nodes set
    const newHiddenNodes = new Set(this.hiddenNodes);
    
    // Update hidden nodes based on check states
    this.collectHiddenNodes(this.rootNodes, newHiddenNodes);
    
    // Emit the changes
    this.nodesVisibilityChanged.emit(newHiddenNodes);
  }
  
  private setNodeChecked(node: TreeNode, checked: boolean): void {
    node.isChecked = checked;
    node.isIndeterminate = false;
    
    // Update all children recursively
    node.children.forEach(child => {
      this.setNodeChecked(child, checked);
    });
  }
  
  private collectHiddenNodes(nodes: TreeNode[], hiddenNodes: Set<string>): void {
    nodes.forEach(node => {
      if (!node.isChecked && !node.isIndeterminate) {
        // Node is unchecked - hide all its node IDs
        node.nodeIds.forEach(nodeId => {
          hiddenNodes.add(nodeId);
        });
      } else if (node.isChecked && !node.isIndeterminate) {
        // Node is fully checked - show all its node IDs
        node.nodeIds.forEach(nodeId => {
          hiddenNodes.delete(nodeId);
        });
      } else if (node.children.length > 0) {
        // Node is indeterminate - recurse to children
        this.collectHiddenNodes(node.children, hiddenNodes);
      }
    });
  }
  
  expandAll(): void {
    this.setAllExpanded(this.rootNodes, true);
  }
  
  collapseAll(): void {
    this.setAllExpanded(this.rootNodes, false);
  }
  
  private setAllExpanded(nodes: TreeNode[], expanded: boolean): void {
    nodes.forEach(node => {
      node.isExpanded = expanded;
      if (node.children.length > 0) {
        this.setAllExpanded(node.children, expanded);
      }
    });
  }
  
  getVisibleNodeCount(): number {
    if (!this.analysisResult) return 0;
    
    const totalNodes = this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace.nodes.length 
      : this.analysisResult.graphs.file.nodes.length;
    
    return totalNodes - this.hiddenNodes.size;
  }
  
  getTotalNodeCount(): number {
    if (!this.analysisResult) return 0;
    
    return this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace.nodes.length 
      : this.analysisResult.graphs.file.nodes.length;
  }
}