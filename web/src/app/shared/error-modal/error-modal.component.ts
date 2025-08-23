import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { ToasterService, ToastMessage } from '../../core/toaster.service';

@Component({
  selector: 'app-error-modal',
  imports: [CommonModule],
  templateUrl: './error-modal.component.html',
  styleUrl: './error-modal.component.css'
})
export class ErrorModalComponent implements OnInit, OnDestroy {
  isVisible = false;
  currentError: ToastMessage | null = null;
  private destroy$ = new Subject<void>();

  constructor(private toasterService: ToasterService) {}

  ngOnInit(): void {
    this.toasterService.toasts$
      .pipe(takeUntil(this.destroy$))
      .subscribe(toasts => {
        // Show modal for error toasts
        const errorToast = toasts.find(toast => toast.type === 'error');
        if (errorToast && !this.isVisible) {
          this.currentError = errorToast;
          this.isVisible = true;
        } else if (!errorToast) {
          this.isVisible = false;
          this.currentError = null;
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  closeModal(): void {
    if (this.currentError) {
      this.toasterService.removeToast(this.currentError.id);
    }
    this.isVisible = false;
    this.currentError = null;
  }

  onBackdropClick(event: MouseEvent): void {
    // Disabled: Users must click "Got it" button to close
    // if (event.target === event.currentTarget) {
    //   this.closeModal();
    // }
  }
}
