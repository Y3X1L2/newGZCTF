import { createContext, useContext, type RefObject } from 'react'
import {
  Link as RouterLink,
  NavLink as RouterNavLink,
  useNavigate as useRouterNavigate,
  useLocation as useRouterLocation,
  useParams as useRouterParams,
  type LinkProps,
  type NavLinkProps,
  type To,
  type NavigateOptions,
} from 'react-router'

export { useSearchParams } from 'react-router'

export const WorkspaceRouteContext = createContext({
  basePath: '/admin/teamlab',
  environmentId: undefined as string | undefined,
  environmentOnly: false,
  element: { current: null } as RefObject<HTMLDivElement | null>,
})
export const useWorkspaceRoute = () => useContext(WorkspaceRouteContext)
function target(to: To, base: string): To {
  if (typeof to === 'string') return to.startsWith('/') ? base + to : to
  return { ...to, pathname: to.pathname?.startsWith('/') ? base + to.pathname : to.pathname }
}
export function Link({ to, ...props }: LinkProps) {
  const { basePath } = useWorkspaceRoute()
  return <RouterLink {...props} to={target(to, basePath)} />
}
export function NavLink({ to, ...props }: NavLinkProps) {
  const { basePath } = useWorkspaceRoute()
  return <RouterNavLink {...props} to={target(to, basePath)} />
}
export function useNavigate() {
  const navigate = useRouterNavigate()
  const { basePath } = useWorkspaceRoute()
  return (to: To, options?: NavigateOptions) => navigate(target(to, basePath), options)
}
export function useLocation() {
  const location = useRouterLocation()
  const { basePath } = useWorkspaceRoute()
  return { ...location, pathname: location.pathname.slice(basePath.length) || '/environments' }
}
export function useParams() {
  const params = useRouterParams()
  const { environmentId } = useWorkspaceRoute()
  return { ...params, id: params.id ?? environmentId }
}
