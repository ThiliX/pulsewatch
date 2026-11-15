import { Compass } from 'lucide-react'
import { ButtonLink } from '../components/Button.tsx'
import { EmptyState } from '../components/States.tsx'

export function NotFoundPage() {
  return (
    <div className="py-10">
      <h1 className="sr-only">Page not found</h1>
      <EmptyState
        title="Page not found"
        icon={Compass}
        action={<ButtonLink to="/">Back to the dashboard</ButtonLink>}
      >
        This page doesn't exist or was moved.
      </EmptyState>
    </div>
  )
}
