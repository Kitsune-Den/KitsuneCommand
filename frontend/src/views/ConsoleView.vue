<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick } from 'vue'
import { useWebSocket } from '@/composables/useWebSocket'
import { usePermissions } from '@/composables/usePermissions'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'

const ws = useWebSocket()
const { canExecuteCommands } = usePermissions()
const commandInput = ref('')
const outputLines = ref<{ type: 'command' | 'output' | 'log' | 'error'; text: string }[]>([])
const outputEl = ref<HTMLElement | null>(null)
const commandHistory = ref<string[]>([])
const historyIndex = ref(-1)

function scrollToBottom() {
  nextTick(() => {
    if (outputEl.value) {
      outputEl.value.scrollTop = outputEl.value.scrollHeight
    }
  })
}

function sendCommand() {
  const cmd = commandInput.value.trim()
  if (!cmd) return

  outputLines.value.push({ type: 'command', text: `> ${cmd}` })
  ws.send(cmd)

  commandHistory.value.unshift(cmd)
  if (commandHistory.value.length > 100) commandHistory.value.pop()
  historyIndex.value = -1
  commandInput.value = ''
  scrollToBottom()
}

function handleKeyDown(e: KeyboardEvent) {
  if (e.key === 'ArrowUp') {
    e.preventDefault()
    if (historyIndex.value < commandHistory.value.length - 1) {
      historyIndex.value++
      commandInput.value = commandHistory.value[historyIndex.value]
    }
  } else if (e.key === 'ArrowDown') {
    e.preventDefault()
    if (historyIndex.value > 0) {
      historyIndex.value--
      commandInput.value = commandHistory.value[historyIndex.value]
    } else {
      historyIndex.value = -1
      commandInput.value = ''
    }
  }
}

onMounted(() => {
  ws.on<{ command: string; output: string }>('CommandResult', (data) => {
    if (data.output) {
      for (const line of data.output.split('\n')) {
        outputLines.value.push({ type: 'output', text: line })
      }
    }
    scrollToBottom()
  })

  ws.on<{ message: string; logLevel: string }>('LogCallback', (data) => {
    const type = data.logLevel === 'Error' || data.logLevel === 'Exception' ? 'error' : 'log'
    outputLines.value.push({ type, text: data.message })
    // Keep buffer manageable
    if (outputLines.value.length > 2000) {
      outputLines.value = outputLines.value.slice(-1500)
    }
    scrollToBottom()
  })

  ws.connect()

  outputLines.value.push({
    type: 'log',
    text: 'KitsuneCommand Console - Type commands below. Use Up/Down arrows for command history.',
  })
})

onUnmounted(() => {
  ws.disconnect()
})
</script>

<template>
  <div class="console-view">
    <div class="page-header">
      <h1 class="page-title">Console</h1>
      <div class="connection-status" :class="{ connected: ws.isConnected.value }">
        <i class="pi pi-circle-fill" />
        {{ ws.isConnected.value ? 'Connected' : 'Disconnected' }}
      </div>
    </div>

    <div class="console-container">
      <div ref="outputEl" class="console-output">
        <div
          v-for="(line, i) in outputLines"
          :key="i"
          class="console-line"
          :class="`console-line--${line.type}`"
        >{{ line.text }}</div>
      </div>

      <div class="console-input-bar">
        <span class="prompt">&gt;</span>
        <InputText
          v-model="commandInput"
          :placeholder="canExecuteCommands ? 'Enter command...' : 'Command execution requires admin access'"
          class="console-input"
          @keydown.enter="sendCommand"
          @keydown="handleKeyDown"
          :disabled="!ws.isConnected.value || !canExecuteCommands"
        />
        <Button
          icon="pi pi-send"
          severity="info"
          @click="sendCommand"
          :disabled="!ws.isConnected.value || !commandInput.trim() || !canExecuteCommands"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.console-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 3rem);
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
}

.connection-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
}

.connection-status i {
  font-size: 0.6rem;
  color: #ef4444;
}

.connection-status.connected i {
  color: #22c55e;
}

.console-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  background: #0d1117;
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  overflow: hidden;
  min-height: 0;
}

.console-output {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
  font-size: 0.85rem;
  line-height: 1.5;
}

.console-line {
  white-space: pre-wrap;
  word-break: break-all;
}

.console-line--command {
  color: var(--kc-cyan);
  font-weight: 600;
}

.console-line--output {
  color: #e6edf3;
}

.console-line--log {
  color: #8b949e;
}

.console-line--error {
  color: #f85149;
}

.console-input-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background: #161b22;
  border-top: 1px solid #30363d;
}

.prompt {
  color: var(--kc-cyan);
  font-family: monospace;
  font-weight: 700;
  font-size: 1rem;
}

.console-input {
  flex: 1;
  background: transparent;
  border: none;
  color: #e6edf3;
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
  font-size: 0.85rem;
}

.console-input:focus {
  box-shadow: none;
}

@media (max-width: 768px) {
  .console-output { overflow-x: auto; }
}
</style>
