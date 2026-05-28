import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DownloadHintStore } from '../download-hint.store';
import { TPipe } from '../i18n/i18n';

@Component({
  selector: 'app-download-hint-toast',
  standalone: true,
  imports: [CommonModule, TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './download-hint-toast.component.html',
  styleUrl: './download-hint-toast.component.css',
})
export class DownloadHintToastComponent {
  readonly downloadHint = inject(DownloadHintStore);

  close(): void {
    this.downloadHint.clear();
  }
}
