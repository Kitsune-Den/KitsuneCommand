<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { getTickets, getTicketDetail, replyToTicket, updateTicketStatus } from '@/api/tickets'
import type { Ticket, TicketDetail } from '@/types'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import Select from 'primevue/select'

const { t } = useI18n()
const toast = useToast()

const tickets = ref<Ticket[]>([])
const totalTickets = ref(0)
const selectedTicket = ref<TicketDetail | null>(null)
const loading = ref(false)
const loadingDetail = ref(false)
const replyText = ref('')
const sending = ref(false)
const searchQuery = ref('')
const statusFilter = ref<string | null>(null)
const pageIndex = ref(0)
const pageSize = 50

const statusOptions = computed(() => [
  { label: t('tickets.allStatuses'), value: null },
  { label: t('tickets.open'), value: 'open' },
  { label: t('tickets.inProgress'), value: 'in_progress' },
  { label: t('tickets.closed'), value: 'closed' },
])

function statusSeverity(status: string): string {
  switch (status) {
    case 'open': return 'success'
    case 'in_progress': return 'warn'
    case 'closed': return 'secondary'
    default: return 'info'
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case 'open': return t('tickets.open')
    case 'in_progress': return t('tickets.inProgress')
    case 'closed': return t('tickets.closed')
    default: return status
  }
}

function priorityLabel(priority: number): string {
  switch (priority) {
    case 0: return t('tickets.low')
    case 2: return t('tickets.high')
    default: return t('tickets.normal')
  }
}

function prioritySeverity(priority: number): string {
  switch (priority) {
    case 0: return 'secondary'
    case 2: return 'danger'
    default: return 'info'
  }
}

function formatDate(dateStr: string): string {
  try {
    const date = new Date(dateStr + (dateStr.endsWith('Z') ? '' : 'Z'))
    return date.toLocaleString(undefined, {
      month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit'
    })
  } catch {
    return ''
  }
}

async function loadTickets() {
  loading.value = true
  try {
    const result = await getTickets({
      pageIndex: pageIndex.value,
      pageSize,
      status: statusFilter.value || undefined,
      search: searchQuery.value.trim() || undefined,
    })
    tickets.value = result.items
    totalTickets.value = result.total
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('tickets.failedToLoad'), life: 3000 })
  } finally {
    loading.value = false
  }
}

async function selectTicket(ticket: Ticket) {
  loadingDetail.value = true
  try {
    selectedTicket.value = await getTicketDetail(ticket.id)
    replyText.value = ''
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('tickets.failedToLoad'), life: 3000 })
  } finally {
    loadingDetail.value = false
  }
}

async function sendReply() {
  if (!selectedTicket.value || !replyText.value.trim() || sending.value) return

  sending.value = true
  try {
    const result = await replyToTicket(selectedTicket.value.id, replyText.value.trim())
    toast.add({
      severity: 'success',
      summary: t('tickets.replySent'),
      detail: result.delivered ? t('tickets.deliveredNow') : t('tickets.queuedForDelivery'),
      life: 3000,
    })
    replyText.value = ''
    // Reload the ticket detail to show the new message
    await selectTicket(selectedTicket.value)
    await loadTickets()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('tickets.failedToReply'), life: 3000 })
  } finally {
    sending.value = false
  }
}

async function changeStatus(status: string) {
  if (!selectedTicket.value) return
  try {
    await updateTicketStatus(selectedTicket.value.id, status)
    toast.add({ severity: 'success', summary: t('tickets.statusUpdated'), life: 2000 })
    await selectTicket(selectedTicket.value)
    await loadTickets()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('tickets.failedToUpdate'), life: 3000 })
  }
}

let searchDebounce: ReturnType<typeof setTimeout> | null = null
function onSearchInput() {
  if (searchDebounce) clearTimeout(searchDebounce)
  searchDebounce = setTimeout(() => {
    pageIndex.value = 0
    loadTickets()
  }, 400)
}

watch(statusFilter, () => {
  pageIndex.value = 0
  loadTickets()
})

onMounted(() => {
  loadTickets()
})
</script>

<template>
  <div class="tickets-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('tickets.title') }}</h1>
      <div class="ticket-controls">
        <span class="p-input-icon-left">
          <i class="pi pi-search" />
          <InputText
            v-model="searchQuery"
            :placeholder="t('tickets.searchPlaceholder')"
            class="search-input"
            @input="onSearchInput"
          />
        </span>
        <Select
          v-model="statusFilter"
          :options="statusOptions"
          optionLabel="label"
          optionValue="value"
          :placeholder="t('tickets.allStatuses')"
          class="status-filter"
        />
      </div>
    </div>

    <div class="tickets-layout">
      <!-- Ticket List -->
      <div class="ticket-list-panel">
        <div v-if="loading && tickets.length === 0" class="loading-state">
          <i class="pi pi-spin pi-spinner" style="font-size: 1.5rem" />
        </div>

        <div v-else-if="tickets.length === 0" class="empty-state">
          <i class="pi pi-ticket" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>{{ t('tickets.noTickets') }}</p>
        </div>

        <div
          v-for="ticket in tickets"
          :key="ticket.id"
          class="ticket-item"
          :class="{ active: selectedTicket?.id === ticket.id }"
          @click="selectTicket(ticket)"
        >
          <div class="ticket-item-header">
            <span class="ticket-id">#{{ ticket.id }}</span>
            <Tag :value="statusLabel(ticket.status)" :severity="statusSeverity(ticket.status) as any" class="status-tag" />
          </div>
          <div class="ticket-subject">{{ ticket.subject }}</div>
          <div class="ticket-meta">
            <span class="ticket-player">{{ ticket.playerName || ticket.playerId }}</span>
            <span class="ticket-date">{{ formatDate(ticket.updatedAt) }}</span>
          </div>
        </div>

        <!-- Pagination -->
        <div v-if="totalTickets > pageSize" class="pagination">
          <Button
            icon="pi pi-chevron-left"
            text
            size="small"
            :disabled="pageIndex === 0"
            @click="pageIndex--; loadTickets()"
          />
          <span class="page-info">{{ pageIndex + 1 }} / {{ Math.ceil(totalTickets / pageSize) }}</span>
          <Button
            icon="pi pi-chevron-right"
            text
            size="small"
            :disabled="(pageIndex + 1) * pageSize >= totalTickets"
            @click="pageIndex++; loadTickets()"
          />
        </div>
      </div>

      <!-- Ticket Detail -->
      <div class="ticket-detail-panel">
        <div v-if="loadingDetail" class="loading-state">
          <i class="pi pi-spin pi-spinner" style="font-size: 1.5rem" />
        </div>

        <div v-else-if="!selectedTicket" class="empty-state">
          <i class="pi pi-arrow-left" style="font-size: 1.5rem; color: var(--kc-text-secondary)" />
          <p>{{ t('tickets.selectTicket') }}</p>
        </div>

        <template v-else>
          <!-- Ticket Header -->
          <div class="detail-header">
            <div class="detail-title-row">
              <h2 class="detail-title">#{{ selectedTicket.id }} {{ selectedTicket.subject }}</h2>
              <Tag :value="priorityLabel(selectedTicket.priority)" :severity="prioritySeverity(selectedTicket.priority) as any" />
            </div>
            <div class="detail-meta">
              <span>{{ selectedTicket.playerName || selectedTicket.playerId }}</span>
              <span class="meta-dot">·</span>
              <span>{{ formatDate(selectedTicket.createdAt) }}</span>
              <span v-if="selectedTicket.assignedTo" class="meta-dot">·</span>
              <span v-if="selectedTicket.assignedTo">{{ t('tickets.assignedTo') }}: {{ selectedTicket.assignedTo }}</span>
            </div>
            <div class="detail-actions">
              <Button
                v-if="selectedTicket.status !== 'in_progress'"
                :label="t('tickets.markInProgress')"
                icon="pi pi-clock"
                severity="warn"
                size="small"
                outlined
                @click="changeStatus('in_progress')"
              />
              <Button
                v-if="selectedTicket.status !== 'closed'"
                :label="t('tickets.close')"
                icon="pi pi-check"
                severity="secondary"
                size="small"
                outlined
                @click="changeStatus('closed')"
              />
              <Button
                v-if="selectedTicket.status === 'closed'"
                :label="t('tickets.reopen')"
                icon="pi pi-refresh"
                severity="success"
                size="small"
                outlined
                @click="changeStatus('open')"
              />
            </div>
          </div>

          <!-- Message Thread -->
          <div class="message-thread">
            <div
              v-for="msg in selectedTicket.messages"
              :key="msg.id"
              class="thread-message"
              :class="{ 'admin-message': msg.senderType === 'admin' }"
            >
              <div class="msg-header">
                <Tag
                  :value="msg.senderType === 'admin' ? t('tickets.admin') : t('tickets.player')"
                  :severity="msg.senderType === 'admin' ? 'info' : 'success'"
                  class="sender-tag"
                />
                <span class="msg-sender">{{ msg.senderName || msg.senderId }}</span>
                <span class="msg-time">{{ formatDate(msg.createdAt) }}</span>
              </div>
              <div class="msg-body">{{ msg.message }}</div>
            </div>
          </div>

          <!-- Reply Box -->
          <div class="reply-box">
            <Textarea
              v-model="replyText"
              :placeholder="t('tickets.replyPlaceholder')"
              rows="3"
              autoResize
              class="reply-input"
              @keydown.ctrl.enter="sendReply"
            />
            <div class="reply-actions">
              <span class="reply-hint">Ctrl + Enter</span>
              <Button
                :label="t('tickets.sendReply')"
                icon="pi pi-send"
                severity="info"
                size="small"
                :disabled="!replyText.trim() || sending"
                :loading="sending"
                @click="sendReply"
              />
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tickets-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 3rem);
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.page-title { font-size: 1.5rem; font-weight: 600; }

.ticket-controls {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.search-input { width: 220px; font-size: 0.85rem; }
.status-filter { width: 160px; font-size: 0.85rem; }

.tickets-layout {
  flex: 1;
  display: flex;
  gap: 1rem;
  min-height: 0;
}

/* ── List Panel ── */
.ticket-list-panel {
  width: 360px;
  min-width: 280px;
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}

.ticket-item {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--kc-border);
  cursor: pointer;
  transition: background 0.15s;
}

.ticket-item:hover { background: rgba(0, 212, 255, 0.05); }
.ticket-item.active { background: rgba(0, 212, 255, 0.12); border-left: 3px solid var(--kc-cyan); }

.ticket-item-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.25rem;
}

.ticket-id {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--kc-text-secondary);
  font-family: 'JetBrains Mono', monospace;
}

.status-tag { font-size: 0.6rem; padding: 0.1rem 0.4rem; }

.ticket-subject {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--kc-text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  margin-bottom: 0.25rem;
}

.ticket-meta {
  display: flex;
  justify-content: space-between;
  font-size: 0.7rem;
  color: var(--kc-text-secondary);
}

.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.5rem;
  border-top: 1px solid var(--kc-border);
}

.page-info { font-size: 0.8rem; color: var(--kc-text-secondary); }

/* ── Detail Panel ── */
.ticket-detail-panel {
  flex: 1;
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.detail-header {
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--kc-border);
}

.detail-title-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.detail-title {
  font-size: 1.1rem;
  font-weight: 600;
  line-height: 1.3;
}

.detail-meta {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  margin-bottom: 0.75rem;
}

.meta-dot { opacity: 0.5; }

.detail-actions {
  display: flex;
  gap: 0.5rem;
}

/* ── Message Thread ── */
.message-thread {
  flex: 1;
  overflow-y: auto;
  padding: 1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.thread-message {
  padding: 0.75rem;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid var(--kc-border);
}

.thread-message.admin-message {
  background: rgba(59, 130, 246, 0.06);
  border-color: rgba(59, 130, 246, 0.2);
}

.msg-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.4rem;
}

.sender-tag { font-size: 0.6rem; padding: 0.1rem 0.35rem; }

.msg-sender {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--kc-text-primary);
}

.msg-time {
  font-size: 0.7rem;
  color: var(--kc-text-secondary);
  margin-left: auto;
}

.msg-body {
  font-size: 0.9rem;
  color: var(--kc-text-primary);
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}

/* ── Reply Box ── */
.reply-box {
  padding: 0.75rem 1.25rem;
  border-top: 1px solid var(--kc-border);
  background: var(--kc-bg-surface);
}

.reply-input { width: 100%; font-size: 0.9rem; margin-bottom: 0.5rem; }

.reply-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.75rem;
}

.reply-hint {
  font-size: 0.7rem;
  color: var(--kc-text-secondary);
  opacity: 0.7;
}

/* ── Shared States ── */
.loading-state {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 3rem;
  color: var(--kc-text-secondary);
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 3rem;
  color: var(--kc-text-secondary);
  font-size: 0.9rem;
}

/* ── Responsive ── */
@media (max-width: 768px) {
  .tickets-layout { flex-direction: column; }
  .ticket-list-panel { width: 100%; max-height: 300px; }
  .ticket-controls { flex-wrap: wrap; width: 100%; }
  .search-input { width: auto; flex: 1; min-width: 120px; }
  .status-filter { width: auto; flex: 1; min-width: 120px; }
}
</style>
