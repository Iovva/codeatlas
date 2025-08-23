import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { StateService } from '../../core/state.service';
import { AnalysisResult } from '../../core/api.service';

interface Cycle {
  id: number;
  size: number;
  sample: string[];
}

@Component({
  selector: 'app-cycles',
  imports: [CommonModule, RouterLink],
  templateUrl: './cycles.html',
  styleUrl: './cycles.css'
})
export class Cycles implements OnInit, OnDestroy {
  cycles: Cycle[] = [];
  analysisResult: AnalysisResult | null = null;
  
  private destroy$ = new Subject<void>();

  constructor(private stateService: StateService) {}
  
  ngOnInit(): void {
    // Subscribe to analysis results
    this.stateService.state$
      .pipe(takeUntil(this.destroy$))
      .subscribe(state => {
        this.analysisResult = state.analysisResult;
        this.cycles = this.analysisResult?.cycles || [];
      });
  }
  
  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  getFileName(filePath: string): string {
    // Extract filename from File:path/to/file.cs format
    const match = filePath.match(/File:.*\/([^\/]+)$/);
    return match ? match[1] : filePath.replace('File:', '');
  }

  viewCycle(cycle: Cycle): void {
    console.log('View cycle will be implemented in Step 13:', cycle);
    // This will navigate to Explorer and isolate the cycle
  }
}
