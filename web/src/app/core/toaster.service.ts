import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export interface ToastMessage {
  id: string;
  type: 'error' | 'warning' | 'success' | 'info';
  title: string;
  message: string;
  details?: string;
  duration?: number;
}

@Injectable({
  providedIn: 'root'
})
export class ToasterService {
  private toastsSubject = new BehaviorSubject<ToastMessage[]>([]);
  public toasts$ = this.toastsSubject.asObservable();

  private toasts: ToastMessage[] = [];

  showRepositoryError(repoUrl: string, detectedLanguages: string[], foundFiles: string[]): void {
    const languageList = detectedLanguages.join('/');
    const fileList = foundFiles.join(', ');
    
    const toast: ToastMessage = {
      id: this.generateId(),
      type: 'error',
      title: 'Repository Not Supported',
      message: `CodeAtlas only analyzes C# repositories. No suitable .sln or .csproj files found.`,
      details: `The repository "${repoUrl}" appears to be a ${languageList} repository.\n\nFound: ${fileList}`,
      duration: 0  // No auto-dismiss for modal errors
    };

    this.addToast(toast);
  }

  showError(title: string, message: string, details?: string): void {
    const toast: ToastMessage = {
      id: this.generateId(),
      type: 'error',
      title,
      message,
      details,
      duration: 6000
    };

    this.addToast(toast);
  }

  showSuccess(title: string, message: string): void {
    const toast: ToastMessage = {
      id: this.generateId(),
      type: 'success',
      title,
      message,
      duration: 4000
    };

    this.addToast(toast);
  }

  showWarning(title: string, message: string): void {
    const toast: ToastMessage = {
      id: this.generateId(),
      type: 'warning',
      title,
      message,
      duration: 5000
    };

    this.addToast(toast);
  }

  removeToast(id: string): void {
    this.toasts = this.toasts.filter(toast => toast.id !== id);
    this.toastsSubject.next([...this.toasts]);
  }

  private addToast(toast: ToastMessage): void {
    this.toasts.push(toast);
    this.toastsSubject.next([...this.toasts]);

    // Only set timeout if duration is specified and greater than 0
    if (toast.duration && toast.duration > 0) {
      setTimeout(() => {
        this.removeToast(toast.id);
      }, toast.duration);
    }
    // If duration is 0 or undefined, toast stays until manually removed
  }

  private generateId(): string {
    return Math.random().toString(36).substr(2, 9);
  }
}
