import { createRouter, createWebHashHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    {
      path: '/login',
      name: 'Login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true },
    },
    {
      path: '/',
      component: () => import('@/components/layout/AppLayout.vue'),
      meta: { requiresAuth: true },
      children: [
        {
          path: '',
          name: 'Dashboard',
          component: () => import('@/views/DashboardView.vue'),
        },
        {
          path: 'players',
          name: 'Players',
          component: () => import('@/views/PlayersView.vue'),
        },
        {
          path: 'players/:entityId',
          name: 'PlayerDetail',
          component: () => import('@/views/PlayerDetailView.vue'),
          props: true,
        },
        {
          path: 'console',
          name: 'Console',
          component: () => import('@/views/ConsoleView.vue'),
        },
        {
          path: 'server',
          name: 'ServerControl',
          component: () => import('@/views/ServerControlView.vue'),
        },
        {
          path: 'config',
          name: 'ConfigEditor',
          component: () => import('@/views/ConfigEditorView.vue'),
        },
        {
          path: 'mods',
          name: 'Mods',
          component: () => import('@/views/ModsView.vue'),
        },
        {
          path: 'backups',
          name: 'Backups',
          component: () => import('@/views/BackupsView.vue'),
        },
        {
          path: 'map',
          name: 'Map',
          component: () => import('@/views/MapView.vue'),
        },
        {
          path: 'chat',
          name: 'Chat',
          component: () => import('@/views/ChatView.vue'),
        },
        {
          path: 'tickets',
          name: 'Tickets',
          component: () => import('@/views/TicketsView.vue'),
        },
        {
          path: 'teleport/cities',
          name: 'TeleportCities',
          component: () => import('@/views/TeleportCitiesView.vue'),
        },
        {
          path: 'teleport/homes',
          name: 'TeleportHomes',
          component: () => import('@/views/TeleportHomesView.vue'),
        },
        {
          path: 'teleport/history',
          name: 'TeleportHistory',
          component: () => import('@/views/TeleportHistoryView.vue'),
        },
        {
          path: 'vipgifts',
          name: 'VipGifts',
          component: () => import('@/views/VipGiftsView.vue'),
        },
        {
          path: 'schedules',
          name: 'Schedules',
          component: () => import('@/views/SchedulesView.vue'),
        },
        {
          path: 'cdkeys',
          name: 'CdKeys',
          component: () => import('@/views/CdKeysView.vue'),
        },
        {
          path: 'cdkeys/redemptions',
          name: 'CdKeyRedemptions',
          component: () => import('@/views/CdKeyRedemptionsView.vue'),
        },
        {
          path: 'economy/points',
          name: 'Points',
          component: () => import('@/views/PointsView.vue'),
        },
        {
          path: 'economy/store',
          name: 'Store',
          component: () => import('@/views/StoreView.vue'),
        },
        {
          path: 'economy/history',
          name: 'PurchaseHistory',
          component: () => import('@/views/PurchaseHistoryView.vue'),
        },
        {
          path: 'items',
          name: 'ItemDatabase',
          component: () => import('@/views/ItemDatabaseView.vue'),
        },
        {
          path: 'settings',
          name: 'Settings',
          component: () => import('@/views/SettingsView.vue'),
        },
      ],
    },
  ],
})

// Navigation guard
router.beforeEach((to, _from, next) => {
  const auth = useAuthStore()

  if (to.meta.public) {
    next()
  } else if (to.meta.requiresAuth && !auth.isAuthenticated) {
    next({ name: 'Login' })
  } else {
    next()
  }
})

export default router
