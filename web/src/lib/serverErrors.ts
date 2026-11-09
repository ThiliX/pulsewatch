import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError, errorMessage } from '../api/client.ts'

/**
 * Puts the server's validation messages on the matching form fields (the server is the authority;
 * client-side checks only mirror it). Returns a message for anything that isn't about a known field.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  fields: readonly Path<T>[],
  setError: UseFormSetError<T>,
): string | undefined {
  if (!(error instanceof ApiError)) {
    return errorMessage(error)
  }

  const leftovers: string[] = []
  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const field = fields.find((f) => f.toLowerCase() === key.toLowerCase())
    const message = messages[0]
    if (!message) continue
    if (field) {
      setError(field, { type: 'server', message }, { shouldFocus: true })
    } else {
      leftovers.push(message)
    }
  }

  if (Object.keys(error.fieldErrors).length === 0) {
    return errorMessage(error)
  }
  return leftovers.length > 0 ? leftovers.join(' ') : undefined
}
