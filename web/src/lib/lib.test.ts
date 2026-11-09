import { describe, expect, it } from 'vitest'
import { describeOutcome, displayUrl, formatMs, formatUptime } from './format.ts'
import { formatDuration, formatRelative } from './time.ts'

describe('formatUptime', () => {
  it.each([
    [null, 'No data'],
    [100, '100.00 %'],
    [99.99999, '99.99 %'],
    [0, '0.00 %'],
    [66.666666, '66.66 %'],
  ])('%s → %s', (input, expected) => {
    expect(formatUptime(input)).toBe(expected)
  })
})

describe('formatting', () => {
  it('formats latency', () => {
    expect(formatMs(null)).toBe('—')
    expect(formatMs(87)).toBe('87 ms')
    expect(formatMs(1530)).toBe('1.5 s')
    expect(formatMs(12_000)).toBe('12 s')
  })

  it('formats durations', () => {
    expect(formatDuration(42)).toBe('42 s')
    expect(formatDuration(600)).toBe('10 min')
    expect(formatDuration(3900)).toBe('1 h 5 min')
    expect(formatDuration(7200)).toBe('2 h')
    expect(formatDuration(93_600)).toBe('1 d 2 h')
  })

  it('formats relative times', () => {
    const now = new Date('2026-11-09T12:00:00Z')
    expect(formatRelative('2026-11-09T11:59:50Z', now)).toBe('just now')
    expect(formatRelative('2026-11-09T11:50:00Z', now)).toBe('10 min ago')
    expect(formatRelative('2026-11-09T09:00:00Z', now)).toBe('3 h ago')
    expect(formatRelative('2026-11-07T12:00:00Z', now)).toBe('2 days ago')
    expect(formatRelative('2026-11-09T12:05:00Z', now)).toBe('in 5 min')
  })

  it('hides query strings, which may hold API keys', () => {
    expect(displayUrl('https://api.example.com/health?api_key=SECRET')).toBe(
      'https://api.example.com/health?…',
    )
    expect(displayUrl('https://api.example.com/health')).toBe('https://api.example.com/health')
  })

  it('describes outcomes in words', () => {
    expect(describeOutcome('HttpStatusFailure', 503)).toBe('Unexpected status 503')
    expect(describeOutcome('Success', 200)).toBe('200 OK')
    expect(describeOutcome('Timeout', null)).toBe('Timed out')
  })
})
