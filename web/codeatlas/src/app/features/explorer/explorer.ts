import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface AnalysisResult {
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
  metrics: any;
  cycles: any[];
}

@Component({
  selector: 'app-explorer',
  imports: [CommonModule, FormsModule],
  templateUrl: './explorer.html',
  styleUrl: './explorer.css'
})
export class Explorer {
  repoUrl: string = '';
  branch: string = '';
  currentScope: 'namespace' | 'file' = 'namespace';
  searchTerm: string = '';
  
  isAnalyzing: boolean = false;
  analysisResult: AnalysisResult | null = null;

  constructor(private http: HttpClient) {}

  async analyze() {
    if (!this.repoUrl.trim()) {
      alert('Please enter a repository URL');
      return;
    }

    this.isAnalyzing = true;
    
    try {
      const request = {
        repoUrl: this.repoUrl.trim(),
        ...(this.branch.trim() && { branch: this.branch.trim() })
      };

      const response = await this.http.post<AnalysisResult>(
        `${environment.apiBaseUrl}/analyze`, 
        request
      ).toPromise();
      
      this.analysisResult = response || null;
      
      console.log('Analysis complete:', this.analysisResult);
    } catch (error: any) {
      console.error('Analysis failed:', error);
      if (error.error?.error && error.error?.message) {
        alert(`Analysis failed: ${error.error.error} - ${error.error.message}`);
      } else {
        alert('Analysis failed. Please check the repository URL and try again.');
      }
    } finally {
      this.isAnalyzing = false;
    }
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
      alert('No analysis data to save');
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
            this.analysisResult = JSON.parse(e.target.result);
            console.log('Loaded analysis data:', this.analysisResult);
          } catch (error) {
            alert('Invalid JSON file');
          }
        };
        reader.readAsText(file);
      }
    };
    input.click();
  }
}
