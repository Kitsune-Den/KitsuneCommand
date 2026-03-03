<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick, watch } from 'vue'
import { getChatHistory } from '@/api/chat'
import { sendChatMessage } from '@/api/chat'
import { useChatStore } from '@/stores/chat'
import { useAuthStore } from '@/stores/auth'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Tag from 'primevue/tag'

const toast = useToast()
const chatStore = useChatStore()
const authStore = useAuthStore()

const messageInput = ref('')
const searchQuery = ref('')
const selectedChatType = ref<number | null>(null)
const loading = ref(false)
const loadingOlder = ref(false)
const sending = ref(false)
const feedEl = ref<HTMLElement | null>(null)
const isSearchMode = ref(false)
const autoScroll = ref(true)

const isViewer = computed(() => authStore.role === 'viewer')

const chatTypeOptions = [
  { label: 'All Types', value: null },
  { label: 'Global', value: 0 },
  { label: 'Friends', value: 1 },
  { label: 'Party', value: 2 },
  { label: 'Whisper', value: 3 },
]

function chatTypeName(type: number): string {
  switch (type) {
    case 0: return 'Global'
    case 1: return 'Friends'
    case 2: return 'Party'
    case 3: return 'Whisper'
    default: return 'Unknown'
  }
}

function chatTypeSeverity(type: number): string {
  switch (type) {
    case 0: return 'info'
    case 1: return 'success'
    case 2: return 'warn'
    case 3: return 'secondary'
    default: return 'contrast'
  }
}

function formatTime(dateStr: string): string {
  try {
    // Server returns datetime('now') as UTC ISO string
    const date = new Date(dateStr + (dateStr.endsWith('Z') ? '' : 'Z'))
    return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}

function formatDate(dateStr: string): string {
  try {
    const date = new Date(dateStr + (dateStr.endsWith('Z') ? '' : 'Z'))
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  } catch {
    return ''
  }
}

/** Initial load: fetch the most recent page of chat history */
async function loadInitialHistory() {
  loading.value = true
  try {
    const result = await getChatHistory({ pageIndex: 0, pageSize: 50 })
    // History comes newest-first from API, reverse to get chronological order
    const reversed = [...result.items].reverse()
    chatStore.setMessages(reversed, result.total)
    scrollToBottom()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load chat history', life: 3000 })
  } finally {
    loading.value = false
  }
}

/** Load older messages when user scrolls to top */
async function loadOlderMessages() {
  if (loadingOlder.value || !chatStore.hasOlderMessages || isSearchMode.value) return
  loadingOlder.value = true

  const oldScrollHeight = feedEl.value?.scrollHeight ?? 0

  try {
    // Calculate next page based on messages we already have
    const currentCount = chatStore.messages.length
    const pageIndex = Math.floor(currentCount / 50)
    const result = await getChatHistory({ pageIndex, pageSize: 50 })
    const reversed = [...result.items].reverse()
    chatStore.prependHistory(reversed, result.total)

    // Preserve scroll position after prepending
    nextTick(() => {
      if (feedEl.value) {
        const newScrollHeight = feedEl.value.scrollHeight
        feedEl.value.scrollTop = newScrollHeight - oldScrollHeight
      }
    })
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load older messages', life: 3000 })
  } finally {
    loadingOlder.value = false
  }
}

/** Search messages via API */
let searchDebounce: ReturnType<typeof setTimeout> | null = null

function onSearchInput() {
  if (searchDebounce) clearTimeout(searchDebounce)
  searchDebounce = setTimeout(() => {
    performSearch()
  }, 400)
}

async function performSearch() {
  const query = searchQuery.value.trim()
  if (!query && selectedChatType.value === null) {
    // Exit search mode, reload live messages
    isSearchMode.value = false
    chatStore.clearMessages()
    await loadInitialHistory()
    return
  }

  isSearchMode.value = true
  loading.value = true
  try {
    const result = await getChatHistory({
      pageIndex: 0,
      pageSize: 100,
      search: query || undefined,
      chatType: selectedChatType.value,
    })
    const reversed = [...result.items].reverse()
    chatStore.setMessages(reversed, result.total)
    scrollToBottom()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Search failed', life: 3000 })
  } finally {
    loading.value = false
  }
}

function onFilterChange() {
  performSearch()
}

function clearSearch() {
  searchQuery.value = ''
  selectedChatType.value = null
  isSearchMode.value = false
  chatStore.clearMessages()
  loadInitialHistory()
}

/** Send a message */
async function sendMessage() {
  const msg = messageInput.value.trim()
  if (!msg || sending.value || isViewer.value) return

  sending.value = true
  try {
    await sendChatMessage(msg)
    messageInput.value = ''
    // The message will echo back through WebSocket
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Failed to send message'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 3000 })
  } finally {
    sending.value = false
  }
}

function scrollToBottom() {
  nextTick(() => {
    if (feedEl.value) {
      feedEl.value.scrollTop = feedEl.value.scrollHeight
    }
  })
}

function onFeedScroll() {
  if (!feedEl.value) return

  // Auto-scroll detection: if within 100px of bottom, stay pinned
  const { scrollTop, scrollHeight, clientHeight } = feedEl.value
  autoScroll.value = scrollHeight - scrollTop - clientHeight < 100

  // Load older messages when scrolled near top
  if (scrollTop < 50) {
    loadOlderMessages()
  }
}

// Watch for new messages and auto-scroll if pinned
watch(
  () => chatStore.messages.length,
  () => {
    if (autoScroll.value && !isSearchMode.value) {
      scrollToBottom()
    }
  }
)

onMounted(() => {
  loadInitialHistory()
})

onUnmounted(() => {
  if (searchDebounce) clearTimeout(searchDebounce)
})
</script>

<template>
  <div class="chat-view">
    <div class="page-header">
      <h1 class="page-title">Chat</h1>
      <div class="chat-controls">
        <div class="search-bar">
          <span class="p-input-icon-left">
            <i class="pi pi-search" />
            <InputText
              v-model="searchQuery"
              placeholder="Search messages..."
              class="search-input"
              @input="onSearchInput"
            />
          </span>
        </div>
        <Select
          v-model="selectedChatType"
          :options="chatTypeOptions"
          optionLabel="label"
          optionValue="value"
          placeholder="All Types"
          class="type-filter"
          @change="onFilterChange"
        />
        <Button
          v-if="isSearchMode"
          icon="pi pi-times"
          severity="secondary"
          text
          rounded
          @click="clearSearch"
          v-tooltip.bottom="'Clear search'"
        />
      </div>
    </div>

    <div class="chat-container">
      <!-- Loading indicator -->
      <div v-if="loading && chatStore.messages.length === 0" class="loading-state">
        <i class="pi pi-spin pi-spinner" style="font-size: 1.5rem" />
        <span>Loading chat history...</span>
      </div>

      <!-- Search mode banner -->
      <div v-if="isSearchMode" class="search-banner">
        <i class="pi pi-search" />
        <span>Showing search results. Real-time messages paused.</span>
      </div>

      <!-- Older messages loader -->
      <div v-if="loadingOlder" class="older-loader">
        <i class="pi pi-spin pi-spinner" />
        <span>Loading older messages...</span>
      </div>

      <!-- Message feed -->
      <div ref="feedEl" class="message-feed" @scroll="onFeedScroll">
        <div v-if="!loading && chatStore.messages.length === 0" class="empty-state">
          <i class="pi pi-comments" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>No messages yet. Chat messages will appear here in real-time.</p>
        </div>

        <div
          v-for="msg in chatStore.messages"
          :key="msg.id"
          class="message-row"
        >
          <div class="message-meta">
            <Tag
              :value="chatTypeName(msg.chatType)"
              :severity="chatTypeSeverity(msg.chatType) as any"
              class="type-badge"
            />
            <span class="sender-name">{{ msg.senderName }}</span>
            <span class="message-time" :title="msg.createdAt">
              {{ formatDate(msg.createdAt) }} {{ formatTime(msg.createdAt) }}
            </span>
          </div>
          <div class="message-text">{{ msg.message }}</div>
        </div>
      </div>

      <!-- Scroll-to-bottom indicator -->
      <Transition name="fade">
        <button
          v-if="!autoScroll && !isSearchMode"
          class="scroll-bottom-btn"
          @click="autoScroll = true; scrollToBottom()"
        >
          <i class="pi pi-arrow-down" />
          New messages
        </button>
      </Transition>

      <!-- Message input -->
      <div class="message-input-bar">
        <InputText
          v-model="messageInput"
          :placeholder="isViewer ? 'Viewers cannot send messages' : 'Type a message...'"
          class="message-input"
          :disabled="isViewer || sending"
          @keydown.enter="sendMessage"
        />
        <Button
          icon="pi pi-send"
          severity="info"
          @click="sendMessage"
          :disabled="isViewer || sending || !messageInput.trim()"
          :loading="sending"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.chat-view {
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

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
}

.chat-controls {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.search-input {
  width: 220px;
  font-size: 0.85rem;
}

.type-filter {
  width: 140px;
  font-size: 0.85rem;
}

.chat-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  overflow: hidden;
  min-height: 0;
  position: relative;
}

.loading-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 2rem;
  color: var(--kc-text-secondary);
  font-size: 0.9rem;
}

.search-banner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  background: rgba(0, 188, 212, 0.08);
  border-bottom: 1px solid var(--kc-border);
  color: var(--kc-cyan);
  font-size: 0.8rem;
}

.older-loader {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.5rem;
  color: var(--kc-text-secondary);
  font-size: 0.8rem;
}

.message-feed {
  flex: 1;
  overflow-y: auto;
  padding: 0.75rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 3rem 0;
  color: var(--kc-text-secondary);
  font-size: 0.9rem;
}

.message-row {
  padding: 0.4rem 0.5rem;
  border-radius: 4px;
  transition: background 0.15s;
}

.message-row:hover {
  background: rgba(255, 255, 255, 0.03);
}

.message-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.15rem;
}

.type-badge {
  font-size: 0.65rem;
  padding: 0.1rem 0.4rem;
  line-height: 1;
}

.sender-name {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--kc-cyan);
}

.message-time {
  font-size: 0.7rem;
  color: var(--kc-text-secondary);
  margin-left: auto;
}

.message-text {
  font-size: 0.9rem;
  color: var(--kc-text-primary);
  line-height: 1.4;
  padding-left: 0.25rem;
  word-break: break-word;
}

.scroll-bottom-btn {
  position: absolute;
  bottom: 70px;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.4rem 0.75rem;
  background: var(--kc-cyan);
  color: #000;
  border: none;
  border-radius: 20px;
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
  z-index: 10;
}

.scroll-bottom-btn:hover {
  background: #00e5ff;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

.message-input-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background: var(--kc-bg-surface);
  border-top: 1px solid var(--kc-border);
}

.message-input {
  flex: 1;
  font-size: 0.9rem;
}

@media (max-width: 768px) {
  .chat-controls { flex-wrap: wrap; width: 100%; }
  .search-input { width: auto; flex: 1; min-width: 120px; }
  .type-filter { width: auto; flex: 1; min-width: 100px; }
}
</style>
