import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { RunLifecycleStatus, RunStatusInfo } from '../app.models';

const BURST_DURATION_MS = 1300;

interface ConfettiParticle {
  color: number;
  delayMs: number;
  rotation: number;
  x: number;
  y: number;
}

@Component({
  selector: 'app-completion-confetti',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (celebrating()) {
      <div class="confetti" aria-hidden="true">
        @for (particle of particles; track $index) {
          <span
            [class]="'confetti__particle confetti__particle--color-' + particle.color"
            [style.--confetti-delay]="particle.delayMs + 'ms'"
            [style.--confetti-rotation]="particle.rotation + 'deg'"
            [style.--confetti-x]="particle.x + 'vw'"
            [style.--confetti-y]="particle.y + 'vh'"
          ></span>
        }
      </div>
    }
  `,
  styles: `
    :host {
      --confetti-blue: #2563eb;
      --confetti-cyan: #06b6d4;
      --confetti-green: #16a34a;
      --confetti-gold: #eab308;
      --confetti-purple: #7c3aed;
    }

    .confetti {
      position: fixed;
      inset: 0;
      z-index: 90;
      overflow: hidden;
      pointer-events: none;
    }

    .confetti__particle {
      position: absolute;
      top: 38%;
      left: 50%;
      width: 0.48rem;
      height: 0.82rem;
      border-radius: 0.12rem;
      background: var(--confetti-blue);
      opacity: 0;
      will-change: transform, opacity;
      animation: confetti-burst 1050ms cubic-bezier(0.16, 0.84, 0.32, 1) var(--confetti-delay) both;
    }

    .confetti__particle--color-1 {
      background: var(--confetti-cyan);
    }

    .confetti__particle--color-2 {
      background: var(--confetti-green);
    }

    .confetti__particle--color-3 {
      background: var(--confetti-gold);
    }

    .confetti__particle--color-4 {
      background: var(--confetti-purple);
    }

    @keyframes confetti-burst {
      0% {
        opacity: 0;
        transform: translate3d(-50%, 0, 0) rotate(0deg) scale(0.65);
      }

      10% {
        opacity: 1;
      }

      100% {
        opacity: 0;
        transform: translate3d(calc(-50% + var(--confetti-x)), var(--confetti-y), 0)
          rotate(var(--confetti-rotation)) scale(1);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .confetti {
        display: none;
      }
    }
  `,
})
export class CompletionConfettiComponent {
  readonly run = input<RunStatusInfo | null>(null);
  readonly celebrating = signal(false);
  readonly particles = createParticles();

  private readonly destroyRef = inject(DestroyRef);
  private destroyed = false;
  private previousRun: Pick<RunStatusInfo, 'correlationId' | 'status'> | null = null;
  private hideTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const currentRun = this.run();

      if (
        currentRun?.status === RunLifecycleStatus.Completed &&
        this.previousRun?.correlationId === currentRun.correlationId &&
        this.previousRun.status !== RunLifecycleStatus.Completed
      ) {
        this.showBurst();
      }

      this.previousRun = currentRun
        ? { correlationId: currentRun.correlationId, status: currentRun.status }
        : null;
    });

    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.clearHideTimer();
    });
  }

  private showBurst(): void {
    this.clearHideTimer();
    this.celebrating.set(false);

    // Новый DOM-узел гарантирует повторный запуск CSS-анимации для следующей выгрузки.
    queueMicrotask(() => {
      if (this.destroyed) {
        return;
      }
      this.celebrating.set(true);
      this.hideTimer = setTimeout(() => this.celebrating.set(false), BURST_DURATION_MS);
    });
  }

  private clearHideTimer(): void {
    if (this.hideTimer !== null) {
      clearTimeout(this.hideTimer);
      this.hideTimer = null;
    }
  }
}

function createParticles(): ConfettiParticle[] {
  return Array.from({ length: 30 }, (_, index) => {
    const side = index % 2 === 0 ? -1 : 1;
    return {
      color: index % 5,
      delayMs: (index % 6) * 22,
      rotation: side * (210 + ((index * 47) % 310)),
      x: side * (8 + ((index * 17) % 43)),
      y: 20 + ((index * 13) % 46),
    };
  });
}
