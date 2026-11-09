import { useLocation } from 'react-router'

/** Exposes the current route so tests can assert on navigation. */
export function LocationProbe() {
  const location = useLocation()
  return (
    <output data-testid="location" hidden>
      {location.pathname}
      {location.search}
    </output>
  )
}
