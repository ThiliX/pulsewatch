import type { IncidentListParams, MonitorListParams } from './endpoints.ts'
import type { StatsWindowName } from './types.ts'

/** Every query key in one place, so mutations invalidate the right data. */
export const queryKeys = {
  me: ['me'] as const,
  summary: ['dashboard', 'summary'] as const,
  monitors: ['monitors'] as const,
  monitorList: (params: MonitorListParams) => ['monitors', 'list', params] as const,
  monitor: (id: string) => ['monitors', id] as const,
  monitorStats: (id: string) => ['monitors', id, 'stats'] as const,
  monitorSeries: (id: string, window: StatsWindowName) =>
    ['monitors', id, 'series', window] as const,
  monitorChecks: (id: string, page: number) => ['monitors', id, 'checks', page] as const,
  monitorIncidents: (id: string, page: number) => ['monitors', id, 'incidents', page] as const,
  incidents: ['incidents'] as const,
  incidentList: (params: IncidentListParams) => ['incidents', 'list', params] as const,
  incident: (id: string) => ['incidents', id] as const,
}
