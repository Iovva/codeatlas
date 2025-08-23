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
            {{ node.isExpanded ? 'remove' : 'add' }}
          </span>
        </button>
        
        <!-- Checkbox and Label -->
        <div class="node-content-wrapper">
          <!-- Standalone Checkbox -->
          <input class="form-check-input" 
                 type="checkbox" 
                 [checked]="node.isChecked"
                 [indeterminate]="node.isIndeterminate"
                 (change)="onCheckboxChange($event)"
                 (click)="$event.stopPropagation()"
                 [id]="'checkbox-' + node.id">
          
          <!-- Clickable Label Area (separate from checkbox) -->
          <div class="node-label-area" 
               (click)="onLabelClick($event)">
            <span class="material-icons me-1">
              {{ getNodeIcon() }}
            </span>
            {{ node.label }}
            <span class="node-count" *ngIf="node.nodeIds.size > 1">
              ({{ node.nodeIds.size }})
            </span>
          </div>
        </div>
      </div>
      
      <!-- Children -->
      <div class="tree-children" *ngIf="node.isExpanded && node.children.length > 0">
                  <app-tree-node 
            *ngFor="let child of node.children"
            [node]="child"
            [level]="level + 1"
            (nodeToggled)="nodeToggled.emit($event)"
            (nodeChecked)="nodeChecked.emit($event)"
            (nodeSelected)="nodeSelected.emit($event)">
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
    
    .node-content-wrapper {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    
    .form-check-input {
      margin: 0;
      cursor: pointer;
    }
    
    .node-label-area {
      display: flex;
      align-items: center;
      color: #ffffff;
      font-size: 0.875rem;
      cursor: pointer;
      padding: 0.125rem 0.25rem;
      border-radius: 3px;
      transition: background-color 0.2s ease;
      flex: 1;
    }
    
    .node-label-area:hover {
      background-color: rgba(255, 255, 255, 0.1);
    }
    
    .node-label-area .material-icons {
      font-size: 1rem;
    }
    
    .node-count {
      color: #ffffff;
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
  @Output() nodeSelected = new EventEmitter<TreeNode>();
  
  toggleNode(): void {
    this.nodeToggled.emit(this.node);
  }
  
  onCheckboxChange(event: any): void {
    this.nodeChecked.emit({ node: this.node, checked: event.target.checked });
  }
  
  onLabelClick(event: Event): void {
    // Prevent event from bubbling up
    event.stopPropagation();
    this.nodeSelected.emit(this.node);
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
          <button type="button" class="btn btn-sm btn-outline-secondary tree-action-btn"
                  (click)="expandAll()" title="Expand All">
            <span class="material-icons">expand_more</span>
          </button>
          <button type="button" class="btn btn-sm btn-outline-secondary tree-action-btn"
                  (click)="collapseAll()" title="Collapse All">
            <span class="material-icons">expand_less</span>
          </button>
        </div>
      </div>
      
      <div class="tree-content">
        <div class="tree-stats">
          <small class="tree-stats-text">
            {{ getVisibleNodeCount() }} of {{ getTotalNodeCount() }} visible
          </small>
        </div>
        
        <div class="tree-nodes" *ngIf="rootNodes.length > 0">
          <div *ngFor="let node of rootNodes" class="tree-node-container">
            <app-tree-node 
              [node]="node"
              [level]="0"
              (nodeToggled)="onNodeToggled($event)"
              (nodeChecked)="onNodeChecked($event)"
              (nodeSelected)="onNodeSelected($event)">
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
  @Output() nodeSelected = new EventEmitter<string>(); // Emits the node ID for selection
  
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
    
    // Propagate node IDs up the tree
    this.propagateNodeIds(this.rootNodes);
    
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
    // Initialize all nodes to visible (checked) from hidden nodes
    this.syncCheckboxStatesFromHiddenNodes(this.rootNodes);
    // Update visual states (indeterminate) based on checkbox states
    this.updateVisualStates(this.rootNodes);
  }
  
  /**
   * Synchronizes checkbox states based on the hiddenNodes set.
   * This is the authoritative source of visibility state.
   */
  private syncCheckboxStatesFromHiddenNodes(nodes: TreeNode[]): void {
    nodes.forEach(node => {
      if (node.children.length > 0) {
        // First sync children
        this.syncCheckboxStatesFromHiddenNodes(node.children);
        
        // For parent nodes, they are checked if they are not explicitly hidden
        // Parent remains checked (showing as indeterminate) even if all children are unchecked
        node.isChecked = !this.hiddenNodes.has(node.id);
      } else {
        // Leaf node - checked if none of its node IDs are hidden
        const hasHiddenNodes = Array.from(node.nodeIds).some(id => this.hiddenNodes.has(id));
        node.isChecked = !hasHiddenNodes;
      }
    });
  }
  
  /**
   * Updates only visual states (isIndeterminate) without changing isChecked values.
   * This provides visual feedback about mixed child states.
   */
  private updateVisualStates(nodes: TreeNode[]): void {
    nodes.forEach(node => {
      if (node.children.length > 0) {
        // First update children visual states
        this.updateVisualStates(node.children);
        
        // Calculate visual state based on children
        const checkedChildren = node.children.filter(child => child.isChecked);
        const indeterminateChildren = node.children.filter(child => child.isIndeterminate);
        
        if (node.isChecked) {
          // Parent is checked - show indeterminate if some children are unchecked
          node.isIndeterminate = checkedChildren.length < node.children.length;
        } else {
          // Parent is unchecked - never show indeterminate
          node.isIndeterminate = false;
        }
      } else {
        // Leaf nodes are never indeterminate
        node.isIndeterminate = false;
      }
    });
  }
  
  onNodeToggled(node: TreeNode): void {
    node.isExpanded = !node.isExpanded;
  }
  
  onNodeSelected(node: TreeNode): void {
    // For tree nodes that represent actual graph nodes, emit the first node ID
    if (node.nodeIds.size > 0) {
      const nodeId = Array.from(node.nodeIds)[0];
      console.log(`🎯 Tree node selected: ${node.label} → Selecting graph node: ${nodeId}`);
      this.nodeSelected.emit(nodeId);
    } else {
      console.log(`🎯 Tree node selected: ${node.label} (no graph node to select)`);
    }
  }
  
  onNodeChecked(event: { node: TreeNode, checked: boolean }): void {
    const { node, checked } = event;
    
    console.log(`🔄 Checkbox changed: ${node.label} (${node.id}) → ${checked}`);
    console.log(`📋 Before change - Hidden nodes:`, Array.from(this.hiddenNodes));
    
    // Create new hidden nodes set starting from current state
    const newHiddenNodes = new Set(this.hiddenNodes);
    
    if (node.children.length > 0) {
      // PARENT NODE ACTION
      console.log(`🔧 Parent node action - ${checked ? 'checking' : 'unchecking'} parent and all children`);
      
      if (checked) {
        // Checking parent → show parent + all children
        newHiddenNodes.delete(node.id);
        this.getAllChildNodeIds(node).forEach(id => newHiddenNodes.delete(id));
      } else {
        // Unchecking parent → hide parent + all children
        newHiddenNodes.add(node.id);
        this.getAllChildNodeIds(node).forEach(id => newHiddenNodes.add(id));
      }
    } else {
      // CHILD NODE ACTION (leaf or non-parent)
      console.log(`🔧 Child node action - ${checked ? 'showing' : 'hiding'} only this node`);
      
      if (checked) {
        // Checking child → show child + make all ancestor parents visible
        newHiddenNodes.delete(node.id);
        
        // Recursively make all ancestor parents visible
        this.makeAllAncestorsVisible(node, newHiddenNodes);
      } else {
        // Unchecking child → hide only this child, never change parent
        newHiddenNodes.add(node.id);
      }
    }
    
    console.log(`📋 After change - Hidden nodes:`, Array.from(newHiddenNodes));
    console.log(`🎯 Difference:`, {
      added: Array.from(newHiddenNodes).filter(id => !this.hiddenNodes.has(id)),
      removed: Array.from(this.hiddenNodes).filter(id => !newHiddenNodes.has(id))
    });
    
    // Emit the changes - the parent component will update hiddenNodes and trigger ngOnChanges
    this.nodesVisibilityChanged.emit(newHiddenNodes);
  }
  
  /**
   * Gets all node IDs that belong to this tree node and its descendants.
   * Used for parent actions that affect all children.
   */
  private getAllChildNodeIds(node: TreeNode): string[] {
    const nodeIds: string[] = [];
    
    // Add this node's ID if it represents an actual graph node
    if (node.nodeIds.size > 0) {
      nodeIds.push(...Array.from(node.nodeIds));
    }
    
    // Add all children's node IDs
    node.children.forEach(child => {
      nodeIds.push(...this.getAllChildNodeIds(child));
    });
    
    return nodeIds;
  }
  
  /**
   * Finds the parent tree node of the given node.
   * Used for child actions that might need to make parent visible.
   */
  private findParentNode(targetNode: TreeNode, nodes: TreeNode[]): TreeNode | null {
    for (const node of nodes) {
      // Check if this node is the direct parent
      if (node.children.includes(targetNode)) {
        return node;
      }
      
      // Recursively search in children
      const parent = this.findParentNode(targetNode, node.children);
      if (parent) {
        return parent;
      }
    }
    
    return null;
  }
  
  /**
   * Makes all ancestor parents visible when a child is checked.
   * This ensures the checked child can be seen in the hierarchy.
   */
  private makeAllAncestorsVisible(node: TreeNode, hiddenNodes: Set<string>): void {
    const parent = this.findParentNode(node, this.rootNodes);
    if (parent && hiddenNodes.has(parent.id)) {
      hiddenNodes.delete(parent.id);
      console.log(`🔧 Making ancestor ${parent.label} visible because descendant was checked`);
      
      // Recursively make grandparents visible too
      this.makeAllAncestorsVisible(parent, hiddenNodes);
    }
  }
  
  private getOwnNodeIds(node: TreeNode): string[] {
    if (node.children.length === 0) {
      // Leaf node - all nodeIds belong to it
      return Array.from(node.nodeIds);
    }
    
    // Parent node - find nodeIds that don't belong to any child
    const childNodeIds = new Set<string>();
    node.children.forEach(child => {
      child.nodeIds.forEach(id => childNodeIds.add(id));
    });
    
    return Array.from(node.nodeIds).filter(id => !childNodeIds.has(id));
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
    
    // Count only actual graph nodes that are visible
    const graphNodes = this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace.nodes 
      : this.analysisResult.graphs.file.nodes;
    
    // Only count graph nodes that are not hidden
    const visibleGraphNodes = graphNodes.filter(node => !this.hiddenNodes.has(node.id));
    
    return visibleGraphNodes.length;
  }
  
  getTotalNodeCount(): number {
    if (!this.analysisResult) return 0;
    
    // Total count is the actual graph nodes, not tree organizational nodes
    return this.currentScope === 'namespace' 
      ? this.analysisResult.graphs.namespace.nodes.length 
      : this.analysisResult.graphs.file.nodes.length;
  }
}