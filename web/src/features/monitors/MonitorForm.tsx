import { TriangleAlert } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { useForm, type Path } from 'react-hook-form'
import { isApiError } from '../../api/client.ts'
import type { MonitorInput } from '../../api/types.ts'
import { Button, ButtonLink } from '../../components/Button.tsx'
import { Field, FormError } from '../../components/Field.tsx'
import { mutedText, surface } from '../../components/styles.ts'
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

      <div className={`${surface} divide-y divide-zinc-200 dark:divide-zinc-800`}>
        <Section
          title="Endpoint"
          description="The URL PulseWatch checks and the name you see in lists and emails."
        >
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
            <p
              role="status"
              className="flex items-center gap-1.5 text-sm text-amber-700 dark:text-amber-400"
            >
              <TriangleAlert aria-hidden="true" className="size-4" />
              Changing the URL resets the monitor's state and streaks.
            </p>
          )}
        </Section>

        <Section
          title="Schedule"
          description="How often to check, and how long to wait for an answer."
          grid
        >
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
        </Section>

        <Section
          title="Alerting"
          description="When an incident opens and closes, and which responses count as success."
          grid
        >
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
        </Section>
      </div>

      <div className="flex justify-end gap-2">
        <ButtonLink to={cancelTo}>Cancel</ButtonLink>
        <Button type="submit" variant="primary" busy={isSubmitting}>
          {submitLabel}
        </Button>
      </div>
    </form>
  )
}

/** One group of settings: title and explanation on the left, fields on the right. */
function Section({
  title,
  description,
  grid = false,
  children,
}: {
  title: string
  description: string
  grid?: boolean
  children: ReactNode
}) {
  return (
    <fieldset className="grid gap-x-8 gap-y-4 p-6 md:grid-cols-3">
      <legend className="sr-only">{title}</legend>
      <div aria-hidden="true">
        <p className="text-sm font-semibold text-zinc-900 dark:text-zinc-100">{title}</p>
        <p className={`mt-1 text-sm ${mutedText}`}>{description}</p>
      </div>
      <div className={`md:col-span-2 ${grid ? 'grid gap-4 sm:grid-cols-2' : 'space-y-4'}`}>
        {children}
      </div>
    </fieldset>
  )
}
