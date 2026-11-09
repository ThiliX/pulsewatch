import { useState } from 'react'
import { useForm, type Path } from 'react-hook-form'
import { isApiError } from '../../api/client.ts'
import type { MonitorInput } from '../../api/types.ts'
import { Button, ButtonLink } from '../../components/Button.tsx'
import { Field, FormError } from '../../components/Field.tsx'
import { applyServerErrors } from '../../lib/serverErrors.ts'
import { zodResolver } from '../../lib/zodResolver.ts'
import { LIMITS, monitorSchema } from './monitorSchema.ts'

const FIELDS: readonly Path<MonitorInput>[] = [
  'name',
  'url',
  'intervalSeconds',
  'timeoutMs',
  'failureThreshold',
  'recoveryThreshold',
  'expectedStatusMin',
  'expectedStatusMax',
]

type MonitorFormProps = {
  initial: MonitorInput
  submitLabel: string
  cancelTo: string
  /** Receives all values and the names of the fields the user changed. */
  onSubmit: (values: MonitorInput, changed: (keyof MonitorInput)[]) => Promise<unknown>
  /** Shown next to the form error for a 412 (someone else changed the monitor). */
  onReload?: () => void
  isEdit?: boolean
}

export function MonitorForm({
  initial,
  submitLabel,
  cancelTo,
  onSubmit,
  onReload,
  isEdit = false,
}: MonitorFormProps) {
  const [formError, setFormError] = useState<string>()
  const [conflict, setConflict] = useState(false)
  const form = useForm({ resolver: zodResolver(monitorSchema), defaultValues: initial })
  const { errors, isSubmitting, dirtyFields } = form.formState
  const urlChanged = isEdit && dirtyFields.url === true

  const submit = form.handleSubmit(async (values) => {
    setFormError(undefined)
    setConflict(false)
    const changed = FIELDS.filter((f) => dirtyFields[f]) as (keyof MonitorInput)[]
    try {
      await onSubmit(values, changed)
    } catch (error) {
      if (isApiError(error, 412)) {
        setConflict(true)
      }
      setFormError(applyServerErrors(error, FIELDS, form.setError))
    }
  })

  const number = { valueAsNumber: true } as const

  return (
    <form noValidate onSubmit={submit} className="space-y-6">
      <FormError message={formError} />
      {conflict && onReload && <Button onClick={onReload}>Reload the latest version</Button>}

      <fieldset className="space-y-4">
        <legend className="text-sm font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
          Endpoint
        </legend>
        <Field
          label="Name"
          maxLength={LIMITS.nameMax}
          error={errors.name?.message}
          {...form.register('name')}
        />
        <Field
          label="URL"
          type="url"
          inputMode="url"
          placeholder="https://api.example.com/health"
          hint="PulseWatch sends one GET request per check, without cookies or credentials. Only public addresses on ports 80, 443, 8080 and 8443 are allowed."
          error={errors.url?.message}
          {...form.register('url')}
        />
        {urlChanged && (
          <p role="status" className="text-sm text-amber-800 dark:text-amber-300">
            <span aria-hidden="true">⚠ </span>
            Changing the URL resets the monitor's state and streaks.
          </p>
        )}
      </fieldset>

      <fieldset className="grid gap-4 sm:grid-cols-2">
        <legend className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
          Schedule
        </legend>
        <Field
          label="Check interval (seconds)"
          type="number"
          min={LIMITS.intervalMin}
          max={LIMITS.intervalMax}
          hint="60 to 3600."
          error={errors.intervalSeconds?.message}
          {...form.register('intervalSeconds', number)}
        />
        <Field
          label="Timeout (ms)"
          type="number"
          min={LIMITS.timeoutMin}
          max={LIMITS.timeoutMax}
          hint="1000 to 15000, and shorter than the interval."
          error={errors.timeoutMs?.message}
          {...form.register('timeoutMs', number)}
        />
      </fieldset>

      <fieldset className="grid gap-4 sm:grid-cols-2">
        <legend className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
          Alerting
        </legend>
        <Field
          label="Failure threshold"
          type="number"
          min={LIMITS.thresholdMin}
          max={LIMITS.thresholdMax}
          hint="Failed checks in a row before an incident opens and you get an email."
          error={errors.failureThreshold?.message}
          {...form.register('failureThreshold', number)}
        />
        <Field
          label="Recovery threshold"
          type="number"
          min={LIMITS.thresholdMin}
          max={LIMITS.thresholdMax}
          hint="Successful checks in a row before the incident resolves."
          error={errors.recoveryThreshold?.message}
          {...form.register('recoveryThreshold', number)}
        />
        <Field
          label="Lowest expected status"
          type="number"
          min={LIMITS.statusMin}
          max={LIMITS.statusMax}
          error={errors.expectedStatusMin?.message}
          {...form.register('expectedStatusMin', number)}
        />
        <Field
          label="Highest expected status"
          type="number"
          min={LIMITS.statusMin}
          max={LIMITS.statusMax}
          hint="A response outside this range counts as a failure."
          error={errors.expectedStatusMax?.message}
          {...form.register('expectedStatusMax', number)}
        />
      </fieldset>

      <div className="flex gap-2">
        <Button type="submit" variant="primary" busy={isSubmitting}>
          {submitLabel}
        </Button>
        <ButtonLink to={cancelTo}>Cancel</ButtonLink>
      </div>
    </form>
  )
}
