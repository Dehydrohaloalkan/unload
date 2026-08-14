import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TPipe } from '../../i18n/i18n';
import { HistoryGatewayAttempt } from '../../state/utils/history-projection.models';
import { resolveSenderStatusLabel } from '../../state/utils/labels.util';
import { formatFileCount } from '../../state/utils/pluralize.util';
import { formatTimestamp } from '../../state/utils/time.util';

@Component({
  selector: 'app-gateway-delivery-history',
  standalone: true,
  imports: [TPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './gateway-delivery-history.component.html',
  styleUrl: './gateway-delivery-history.component.css',
})
export class GatewayDeliveryHistoryComponent {
  readonly attempts = input.required<HistoryGatewayAttempt[]>();

  formatTimestamp = formatTimestamp;
  formatFileCount = formatFileCount;
  resolveSenderStatusLabel = resolveSenderStatusLabel;
}
