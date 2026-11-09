import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router'
import { createMonitor, getMonitor, updateMonitor } from '../../api/endpoints.ts'
import { queryKeys } from '../../api/queryKeys.ts'
import type { Monitor, MonitorInput } from '../../api/types.ts'
import { ErrorState, LoadingBlock, PageHeader } from '../../components/States.tsx'
import { useToast } from '../../components/toastContext.ts'
import { MonitorForm } from './MonitorForm.tsx'
import { DEFAULT_MONITOR } from './monitorSchema.ts'

export function MonitorNewPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const toast = useToast()

  return (
    <div className="max-w-2xl space-y-6">
      <PageHeader title="Add monitor">
        The first check runs within a few seconds of saving.
      </PageHeader>
      <MonitorForm
        initial={DEFAULT_MONITOR}
        submitLabel="Create monitor"
        cancelTo="/monitors"
        onSubmit={async (values) => {
          const created = await createMonitor(values)
          await queryClient.invalidateQueries({ queryKey: queryKeys.monitors })
          await queryClient.invalidateQueries({ queryKey: queryKeys.summary })
          toast.show(`"${created.name}" was created.`, 'success')
          void navigate(`/monitors/${created.id}`)
        }}
      />
    </div>
  )
}

function toInput(monitor: Monitor): MonitorInput {
  return {
    name: monitor.name,
    url: monitor.url,
    intervalSeconds: monitor.intervalSeconds,
    timeoutMs: monitor.timeoutMs,
    failureThreshold: monitor.failureThreshold,
    recoveryThreshold: monitor.recoveryThreshold,
    expectedStatusMin: monitor.expectedStatusMin,
    expectedStatusMax: monitor.expectedStatusMax,
  }
}

export function MonitorEditPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const toast = useToast()
  // Always load a fresh copy (and its ETag) for editing, so we never overwrite newer changes blindly.
  const monitor = useQuery({
    queryKey: queryKeys.monitor(id),
    queryFn: ({ signal }) => getMonitor(id, signal),
    staleTime: 0,
    refetchOnWindowFocus: false,
  })

  if (monitor.isPending) return <LoadingBlock label="Loading the monitor" />
  if (monitor.isError) {
    return (
      <ErrorState
        title="Couldn't load the monitor"
        error={monitor.error}
        onRetry={() => void monitor.refetch()}
      />
    )
  }

  const { monitor: current, etag } = monitor.data

  return (
    <div className="max-w-2xl space-y-6">
      <PageHeader title={`Edit ${current.name}`} />
      <MonitorForm
        // Remount with fresh values after "Reload the latest version".
        key={current.version}
        isEdit
        initial={toInput(current)}
        submitLabel="Save changes"
        cancelTo={`/monitors/${id}`}
        onReload={() => void monitor.refetch()}
        onSubmit={async (values, changed) => {
          if (changed.length === 0) {
            void navigate(`/monitors/${id}`)
            return
          }
          const patch = Object.fromEntries(
            changed.map((key) => [key, values[key]]),
          ) as Partial<MonitorInput>
          const updated = await updateMonitor(id, patch, etag)
          await queryClient.invalidateQueries({ queryKey: queryKeys.monitors })
          await queryClient.invalidateQueries({ queryKey: queryKeys.summary })
          toast.show(`"${updated.name}" was saved.`, 'success')
          void navigate(`/monitors/${id}`)
        }}
      />
    </div>
  )
}
