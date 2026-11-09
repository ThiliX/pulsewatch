import { z } from 'zod'
import type { MonitorInput } from '../../api/types.ts'

// Mirrors the server's hard limits (MonitorLimits). The server re-checks everything, including
// whether the URL points to a public address, and its messages are shown on the fields.
export const LIMITS = {
  nameMax: 100,
  urlMax: 2048,
  intervalMin: 60,
  intervalMax: 3600,
  timeoutMin: 1000,
  timeoutMax: 15000,
  thresholdMin: 1,
  thresholdMax: 10,
  statusMin: 100,
  statusMax: 599,
}

const whole = (label: string, min: number, max: number) =>
  z
    .number({ error: `${label} must be a number.` })
    .int(`${label} must be a whole number.`)
    .min(min, `${label} must be between ${min} and ${max}.`)
    .max(max, `${label} must be between ${min} and ${max}.`)

export const monitorSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, 'Name is required.')
      .max(LIMITS.nameMax, `Name must be at most ${LIMITS.nameMax} characters.`)
      // eslint-disable-next-line no-control-regex
      .refine((v) => !/[\u0000-\u001f\u007f]/.test(v), 'Name must not contain control characters.'),
    url: z
      .string()
      .trim()
      .min(1, 'URL is required.')
      .max(LIMITS.urlMax, `URL must be at most ${LIMITS.urlMax} characters.`)
      .refine((v) => {
        try {
          const u = new URL(v)
          return u.protocol === 'http:' || u.protocol === 'https:'
        } catch {
          return false
        }
      }, 'Enter a full http:// or https:// URL.'),
    intervalSeconds: whole('Interval', LIMITS.intervalMin, LIMITS.intervalMax),
    timeoutMs: whole('Timeout', LIMITS.timeoutMin, LIMITS.timeoutMax),
    failureThreshold: whole('Failure threshold', LIMITS.thresholdMin, LIMITS.thresholdMax),
    recoveryThreshold: whole('Recovery threshold', LIMITS.thresholdMin, LIMITS.thresholdMax),
    expectedStatusMin: whole('Status code', LIMITS.statusMin, LIMITS.statusMax),
    expectedStatusMax: whole('Status code', LIMITS.statusMin, LIMITS.statusMax),
  })
  .refine((v) => v.timeoutMs < v.intervalSeconds * 1000, {
    path: ['timeoutMs'],
    message: 'Timeout must be shorter than the check interval.',
  })
  .refine((v) => v.expectedStatusMax >= v.expectedStatusMin, {
    path: ['expectedStatusMax'],
    message: 'The highest expected status must not be lower than the lowest.',
  })

export const DEFAULT_MONITOR: MonitorInput = {
  name: '',
  url: '',
  intervalSeconds: 60,
  timeoutMs: 10_000,
  failureThreshold: 3,
  recoveryThreshold: 1,
  expectedStatusMin: 200,
  expectedStatusMax: 299,
}
