import { Component } from '@angular/core';

@Component({
  selector: 'app-liquid-transition',
  standalone: true,
  template: `
    <div class="liquid-shell" aria-hidden="true">
      <div class="liquid-track liquid-track--left">
        <span class="liquid-stream"></span>
      </div>
      <div class="liquid-track liquid-track--right">
        <span class="liquid-stream"></span>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      width: 100%;
    }

    .liquid-shell {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 2rem;
      padding: 1rem 0 0;
    }

    .liquid-track {
      position: relative;
      height: 7rem;
      overflow: hidden;
      border-radius: 999px;
      border: 1px solid rgba(148, 163, 184, 0.18);
      background:
        linear-gradient(180deg, rgba(255, 255, 255, 0.92), rgba(236, 244, 255, 0.88)),
        radial-gradient(circle at top, rgba(96, 165, 250, 0.12), transparent 68%);
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.75);
    }

    .liquid-track::before {
      content: '';
      position: absolute;
      inset: 0.75rem;
      border-radius: 999px;
      background: linear-gradient(90deg, rgba(186, 230, 253, 0.32), rgba(96, 165, 250, 0.12));
      filter: blur(2px);
    }

    .liquid-stream {
      position: absolute;
      inset: 1rem;
      border-radius: 999px;
      background:
        linear-gradient(90deg, rgba(125, 211, 252, 0.18), rgba(59, 130, 246, 0.74), rgba(125, 211, 252, 0.18));
      background-size: 220% 100%;
      animation: stream 3.4s linear infinite;
      box-shadow:
        0 0 24px rgba(96, 165, 250, 0.32),
        inset 0 0 12px rgba(255, 255, 255, 0.42);
    }

    .liquid-track--right .liquid-stream {
      animation-delay: -1.2s;
    }

    @keyframes stream {
      0% {
        transform: translateX(-24%);
        background-position: 0% 50%;
      }

      100% {
        transform: translateX(24%);
        background-position: 100% 50%;
      }
    }
  `,
})
export class LiquidTransitionComponent {}
