<template>
  <transition name="vella-modal">
    <div
      v-if="visible"
      class="fixed inset-0 z-50 flex items-center justify-center"
      @keydown.esc="emitClose"
      tabindex="0"
    >
      <!-- BACKDROP -->
      <div
        class="absolute inset-0 bg-black/40 backdrop-blur-sm"
        @click="emitClose"
      ></div>

      <!-- MODAL PANEL -->
      <div
        class="relative bg-white/20 backdrop-blur-xl border border-white/20
               rounded-3xl shadow-xl
               p-5 max-w-md w-[90%] text-white
               animate-slide-up"
      >
        <!-- Dynamic content -->
        <slot />

        <!-- BUTTONS (optional, controlled by slot if needed) -->
        <div v-if="showFooter" class="mt-4 flex justify-end gap-2">
            <button
                class="px-3 py-1 rounded-md bg-white/15 border border-white/30
                    text-white text-xs hover:bg-white/25 transition"
                @click="emitCancel"
            >
                Cancel
            </button>

            <button
                class="px-3 py-1 rounded-md bg-[#4EE29B] text-white text-xs font-medium
                    hover:bg-[#6ff2b3] transition"
                @click="emitConfirm"
            >
                Confirm
            </button>
            </div>
      </div>
    </div>
  </transition>
</template>

<script setup>
defineProps({
  visible: Boolean,
  showFooter: Boolean,
})

const emit = defineEmits(["confirm", "cancel", "close"])

function emitConfirm() {
  emit("confirm")
}
function emitCancel() {
  emit("cancel")
}
function emitClose() {
  emit("close")
}
</script>

<style scoped>
/* BACKDROP + PANEL ANIMATIONS */
.vella-modal-enter-active,
.vella-modal-leave-active {
  transition: opacity 0.22s ease;
}

.vella-modal-enter-from,
.vella-modal-leave-to {
  opacity: 0;
}

/* Slide Up Animation */
@keyframes slide-up {
  0% {
    opacity: 0;
    transform: translateY(28px);
  }
  60% {
    opacity: 1;
    transform: translateY(6px);
  }
  100% {
    opacity: 1;
    transform: translateY(0);
  }
}

.animate-slide-up {
  animation: slide-up 0.22s cubic-bezier(0.22, 0.61, 0.36, 1) forwards;
}

</style>
