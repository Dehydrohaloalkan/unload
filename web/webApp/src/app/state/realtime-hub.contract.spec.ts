import { describe, expect, it } from 'vitest';
import { REALTIME_HUB_CONTRACT } from './realtime-hub.contract';

describe('SignalR contract', () => {
  it('keeps the public hub names expected by the backend', () => {
    expect(REALTIME_HUB_CONTRACT).toEqual({
      hubPath: '/hubs/status',
      subscribeMethod: 'SubscribeRun',
      statusEvent: 'status',
      runStatusEvent: 'run_status',
      presetStateEvent: 'preset_state',
      presetReplayedEvent: 'preset_replayed',
    });
  });
});
