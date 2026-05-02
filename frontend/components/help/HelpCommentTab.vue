<template>
  <div class="space-y-5 max-w-2xl">

    <!-- HEADER -->
    <div class="space-y-1">
      <h3 class="text-sm font-bold text-[#CDDC39]">Send a Comment</h3>
      <p class="text-[11px] text-white/60 leading-relaxed">
        Help us improve Vella. Your message goes straight to the IRIs team.
      </p>
    </div>

    <!-- FROM (auto-attached identity) -->
    <div class="bg-white/5 border border-white/10 rounded-xl p-3">
      <div class="text-[9px] uppercase tracking-wider text-white/40 mb-1">From</div>
      <div class="text-xs text-white/80">{{ userName || '(signed-in user)' }}</div>
    </div>

    <!-- CATEGORY -->
    <div>
      <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Category</label>
      <div class="flex flex-wrap gap-2">
        <button v-for="cat in categories" :key="cat"
          type="button"
          @click="selectedCategory = cat"
          class="px-3 py-1.5 rounded-lg text-xs transition border"
          :class="selectedCategory === cat
            ? 'bg-white/20 text-white border-white/40 font-medium'
            : 'bg-white/5 text-white/60 hover:bg-white/10 border-white/15'">
          {{ cat }}
        </button>
      </div>
    </div>

    <!-- MESSAGE -->
    <div>
      <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Message</label>
      <textarea v-model="message"
        rows="6"
        placeholder="Tell us what you think, what's broken, or what you'd like to see…"
        class="w-full bg-white/10 border border-white/20 rounded-xl px-3 py-2 text-xs text-white placeholder-white/40 outline-none focus:border-[#CDDC39] transition resize-none leading-relaxed"></textarea>
      <div class="flex justify-end mt-1">
        <span class="text-[10px] text-white/40">{{ message.length }} / 2000</span>
      </div>
    </div>

    <!-- ACTIONS -->
    <div class="flex items-center justify-end gap-3 pt-1">
      <transition
        enter-active-class="transition duration-200"
        enter-from-class="opacity-0 translate-x-2"
        enter-to-class="opacity-100 translate-x-0"
        leave-active-class="transition duration-150"
        leave-from-class="opacity-100"
        leave-to-class="opacity-0">
        <span v-if="status === 'sent'" class="text-xs text-[#4EE29B] flex items-center gap-1.5">
          <Icon name="lucide:check-circle-2" class="text-sm" />
          Comment sent. Thank you!
        </span>
        <span v-else-if="status === 'error'" class="text-xs text-rose-300 flex items-center gap-1.5">
          <Icon name="lucide:alert-circle" class="text-sm" />
          {{ errorMsg || 'Failed to send. Try again.' }}
        </span>
      </transition>

      <button @click="submit"
        :disabled="!canSubmit"
        class="px-5 py-2 rounded-xl text-xs font-bold transition flex items-center gap-2"
        :class="canSubmit
          ? 'bg-[#CDDC39] hover:bg-[#E6EE9C] text-[#0A1D4A] shadow-lg shadow-blue-900/20'
          : 'bg-white/10 text-white/30 cursor-not-allowed'">
        <Icon v-if="status === 'sending'" name="lucide:loader-2" class="animate-spin text-sm" />
        <Icon v-else name="lucide:send" class="text-sm" />
        <span>{{ status === 'sending' ? 'Sending…' : 'Send' }}</span>
      </button>
    </div>

  </div>
</template>

<script setup>
import { ref, computed } from 'vue';

const props = defineProps({
  userName:     { type: String,   default: '' },
  sessionKey:   { type: String,   default: '' },
  submitDirect: { type: Function, default: null },
});

const categories = ['Bug', 'Feature Request', 'Question', 'Other'];

const selectedCategory = ref('Question');
const message = ref('');
const status = ref('idle');   // 'idle' | 'sending' | 'sent' | 'error'
const errorMsg = ref('');

const MAX_LEN = 2000;

const canSubmit = computed(() =>
  status.value !== 'sending' &&
  !!selectedCategory.value &&
  message.value.trim().length >= 5 &&
  message.value.length <= MAX_LEN
);

async function submit() {
  if (!canSubmit.value) return;

  status.value = 'sending';
  errorMsg.value = '';

  const payload = {
    message:     'send_comment',           // intent for backend fast-route
    category:    selectedCategory.value,
    body:        message.value.trim(),
    user_name:   props.userName,
    session_key: props.sessionKey,
  };

  if (typeof props.submitDirect !== 'function') {
    status.value = 'error';
    errorMsg.value = 'Submit handler not available.';
    return;
  }

  try {
    const data = await props.submitDirect(payload);
    const result = data?.send_comment_result;

    if (result?.status === 'success') {
      status.value = 'sent';
      // Reset the form after the success message has been visible briefly
      setTimeout(() => {
        message.value = '';
        selectedCategory.value = 'Question';
        status.value = 'idle';
      }, 2500);
    } else {
      status.value = 'error';
      errorMsg.value = result?.message || data?.message || 'Failed to send.';
    }
  } catch (err) {
    status.value = 'error';
    errorMsg.value = (err && err.message) || 'Network error.';
  }
}
</script>
