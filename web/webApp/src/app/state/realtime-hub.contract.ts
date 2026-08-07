export const REALTIME_HUB_CONTRACT = {
  hubPath: '/hubs/status',
  subscribeMethod: 'SubscribeRun',
  statusEvent: 'status',
  runStatusEvent: 'run_status',
  presetStateEvent: 'preset_state',
  presetReplayedEvent: 'preset_replayed',
} as const;
