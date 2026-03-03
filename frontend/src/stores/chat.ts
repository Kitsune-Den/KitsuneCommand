import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ChatRecord } from '@/types'

export const useChatStore = defineStore('chat', () => {
  const messages = ref<ChatRecord[]>([])
  const hasOlderMessages = ref(true)

  /** Append a real-time message from WebSocket (to the end). */
  function addRealtimeMessage(record: ChatRecord) {
    messages.value.push(record)
    // Keep buffer at 500 messages max
    if (messages.value.length > 500) {
      messages.value = messages.value.slice(-400)
    }
  }

  /** Prepend older history messages (to the beginning). */
  function prependHistory(records: ChatRecord[], total: number) {
    // Deduplicate by id
    const existingIds = new Set(messages.value.map((m) => m.id))
    const newRecords = records.filter((r) => !existingIds.has(r.id))
    messages.value = [...newRecords, ...messages.value]

    // If we've loaded all records, no more older messages
    hasOlderMessages.value = messages.value.length < total
  }

  /** Replace all messages (used for search results). */
  function setMessages(records: ChatRecord[], total: number) {
    messages.value = records
    hasOlderMessages.value = records.length < total
  }

  function clearMessages() {
    messages.value = []
    hasOlderMessages.value = true
  }

  return { messages, hasOlderMessages, addRealtimeMessage, prependHistory, setMessages, clearMessages }
})
