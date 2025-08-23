import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

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
export class Cycles {
  cycles: Cycle[] = [];

  constructor() {
    // For now, we'll have empty cycles - this will be populated from shared state in Step 12
    // Example data for testing:
    // this.cycles = [
    //   { id: 1, size: 3, sample: ['File:src/Core/A.cs', 'File:src/Core/B.cs', 'File:src/Core/C.cs'] },
    //   { id: 2, size: 2, sample: ['File:src/Utils/Helper.cs', 'File:src/Utils/Validator.cs'] }
    // ];
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
