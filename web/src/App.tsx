import { Route, Routes } from 'react-router'
import { AppLayout } from './components/AppLayout.tsx'
import { LoginPage, RegisterPage } from './features/auth/AuthPages.tsx'
import { RequireAuth } from './features/auth/RequireAuth.tsx'
import { DashboardPage } from './features/dashboard/DashboardPage.tsx'
import { IncidentDetailPage, IncidentListPage } from './features/incidents/IncidentPages.tsx'
import { MonitorDetailPage } from './features/monitors/MonitorDetailPage.tsx'
import { MonitorEditPage, MonitorNewPage } from './features/monitors/MonitorEditPages.tsx'
import { MonitorListPage } from './features/monitors/MonitorListPage.tsx'
import { NotFoundPage } from './pages/NotFoundPage.tsx'

export default function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />
        <Route element={<RequireAuth />}>
          <Route index element={<DashboardPage />} />
          <Route path="monitors" element={<MonitorListPage />} />
          <Route path="monitors/new" element={<MonitorNewPage />} />
          <Route path="monitors/:id" element={<MonitorDetailPage />} />
          <Route path="monitors/:id/edit" element={<MonitorEditPage />} />
          <Route path="incidents" element={<IncidentListPage />} />
          <Route path="incidents/:id" element={<IncidentDetailPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
