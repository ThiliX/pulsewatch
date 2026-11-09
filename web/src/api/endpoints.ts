import { apiGet, apiRequest, isApiError } from './client.ts'
import type {
  Check,
  CheckSeries,
  DashboardSummary,
  Incident,
  IncidentDetail,
  IncidentStatus,
  Me,
  Monitor,
  MonitorInput,
  MonitorState,
  MonitorStats,
  Paged,
  StatsWindowName,
} from './types.ts'

const V1 = '/api/v1'

function query(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined) {
      search.set(key, String(value))
    }
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

// --- Auth ---

/** The signed-in user, or null when not signed in. */
export async function getMe(signal?: AbortSignal): Promise<Me | null> {
  try {
    return await apiGet<Me>(`${V1}/auth/me`, signal)
  } catch (error) {
    if (isApiError(error, 401)) {
      return null
    }
    throw error
  }
}

export type Credentials = { email: string; password: string }

export async function login(credentials: Credentials): Promise<Me> {
  return (await apiRequest<Me>(`${V1}/auth/login`, { method: 'POST', body: credentials })).data
}

export async function register(credentials: Credentials): Promise<Me> {
  return (await apiRequest<Me>(`${V1}/auth/register`, { method: 'POST', body: credentials })).data
}

export async function logout(): Promise<void> {
  await apiRequest<void>(`${V1}/auth/logout`, { method: 'POST' })
}

// --- Dashboard ---

export const getSummary = (signal?: AbortSignal) =>
  apiGet<DashboardSummary>(`${V1}/dashboard/summary`, signal)

// --- Monitors ---

export type MonitorListParams = { page: number; pageSize: number; state?: MonitorState | undefined }

export const listMonitors = (params: MonitorListParams, signal?: AbortSignal) =>
  apiGet<Paged<Monitor>>(`${V1}/monitors${query(params)}`, signal)

/** The monitor plus its ETag, which edits send back as If-Match. */
export async function getMonitor(id: string, signal?: AbortSignal) {
  const { data, etag } = await apiRequest<Monitor>(`${V1}/monitors/${id}`, { signal })
  return { monitor: data, etag }
}

export async function createMonitor(input: MonitorInput): Promise<Monitor> {
  return (await apiRequest<Monitor>(`${V1}/monitors`, { method: 'POST', body: input })).data
}

export async function updateMonitor(
  id: string,
  changes: Partial<MonitorInput>,
  etag: string | null,
): Promise<Monitor> {
  return (
    await apiRequest<Monitor>(`${V1}/monitors/${id}`, {
      method: 'PATCH',
      body: changes,
      ifMatch: etag ?? undefined,
    })
  ).data
}

export async function deleteMonitor(id: string): Promise<void> {
  await apiRequest<void>(`${V1}/monitors/${id}`, { method: 'DELETE' })
}

export type MonitorAction = 'pause' | 'resume' | 'check-now'

export async function monitorAction(id: string, action: MonitorAction): Promise<Monitor> {
  return (await apiRequest<Monitor>(`${V1}/monitors/${id}/${action}`, { method: 'POST' })).data
}

export const getMonitorStats = (id: string, signal?: AbortSignal) =>
  apiGet<MonitorStats>(`${V1}/monitors/${id}/stats`, signal)

export const getCheckSeries = (id: string, window: StatsWindowName, signal?: AbortSignal) =>
  apiGet<CheckSeries>(`${V1}/monitors/${id}/checks/series${query({ window })}`, signal)

export const listChecks = (id: string, page: number, pageSize: number, signal?: AbortSignal) =>
  apiGet<Paged<Check>>(`${V1}/monitors/${id}/checks${query({ page, pageSize })}`, signal)

export const listMonitorIncidents = (
  id: string,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
) => apiGet<Paged<Incident>>(`${V1}/monitors/${id}/incidents${query({ page, pageSize })}`, signal)

// --- Incidents ---

export type IncidentListParams = {
  page: number
  pageSize: number
  status?: Lowercase<IncidentStatus> | undefined
}

export const listIncidents = (params: IncidentListParams, signal?: AbortSignal) =>
  apiGet<Paged<Incident>>(`${V1}/incidents${query(params)}`, signal)

export const getIncident = (id: string, signal?: AbortSignal) =>
  apiGet<IncidentDetail>(`${V1}/incidents/${id}`, signal)
