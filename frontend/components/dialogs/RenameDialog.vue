<template>
  <div class="text-white">

    <!-- Title -->
    <h2 class="text-lg font-semibold mb-3">
      Rename
    </h2>

    <!-- Old Name (readonly) -->
    <div class="mb-4">
      <label class="text-xs opacity-70">Current Name</label>
      <div
        class="mt-1 px-3 py-1.5 rounded-lg bg-white/10 border border-white/20
               text-sm select-text"
      >
        {{ oldName }}
      </div>
    </div>

    <!-- New Name Input -->
    <div>
      <label class="text-xs opacity-70">New Name</label>
      <input
        ref="inputRef"
        v-model="newName"
        type="text"
        class="mt-1 w-full px-3 py-1.5 rounded-lg bg-white/20 border
               border-white/30 focus:outline-none focus:ring-1
               focus:ring-[#4EE29B] text-sm placeholder-white/40"
        placeholder="Enter new name..."
        @keydown.enter.prevent="submit"
      />
    </div>

  </div>
</template>

<script setup>
import { ref, onMounted } from "vue"

const props = defineProps({
  oldName: { type: String, required: true },
})

const emit = defineEmits(["submit"])

const newName = ref("")
const inputRef = ref(null)

onMounted(() => {
  // Autofocus for smooth UX
  inputRef.value?.focus()
})

// Submit to parent modal (index.vue)
function submit() {
  if (!newName.value.trim()) return
  emit("submit", newName.value.trim())
}
</script>

<style scoped>
/* optional small fade for input field */
</style>
