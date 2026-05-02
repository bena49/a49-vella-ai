// composables/useChat.ts
// Extracted from index.vue — Chat messages, backend communication, and utilities

import { ref, nextTick, type Ref } from "vue";

interface ChatMessage {
  from: "user" | "vella";
  text: string;
}

export function useChat(
  getValidToken: () => Promise<string | null>,
  isAuthenticated: Ref<boolean>,
  accessToken: Ref<string>,
  sessionKey: Ref<string | null>,
  sendToRevit: (data: any) => void,
  handleAction: (action: string) => void
) {
  // --- STATE ---
  const messages = ref<ChatMessage[]>([]);
  const isThinking = ref(false);
  const chatContainer = ref<HTMLElement | null>(null);
  const API_URL = "https://a49iris.com/irisai-api/api/ai/";

  // --- SCROLL ---
  async function scrollToBottom() {
    await nextTick();
    if (chatContainer.value)
      chatContainer.value.scrollTop = chatContainer.value.scrollHeight;
  }

  // --- CLEAR ---
  function clearChatAndMemory() {
    messages.value = [];
    messages.value.push({ from: "vella", text: "Chat cleared." });
  }

  // --- SUBMIT ---
  function handleUserSubmit(text: string) {
    messages.value.push({ from: "user", text });
    scrollToBottom();

    const payload: any = { message: text, session_key: sessionKey.value };

    // Attach preflight result if user is confirming a repair
    const confirmWords = [
      "yes", "y", "confirm", "fix", "repair", "proceed", "sure", "ok",
    ];
    if (
      (window as any).__vellaPreflightResult &&
      confirmWords.includes(text.toLowerCase().trim())
    ) {
      payload.preflight_result = (window as any).__vellaPreflightResult;
      (window as any).__vellaPreflightResult = null;
    }

    sendToBackend(payload);
  }

  // --- SEND REVIT DATA ---
  async function sendUserPrompt(revitData: any = null) {
    const payload = revitData || {};
    if (!payload.session_key) payload.session_key = sessionKey.value;
    sendToBackend(payload);
  }

  // --- BACKEND COMMUNICATION ---
  async function sendToBackend(payload: any) {
    isThinking.value = true;

    // 💥 GET FRESH TOKEN BEFORE EVERY REQUEST
    const token = await getValidToken();
    if (!token) {
      isThinking.value = false;
      return; // Stop if not authenticated
    }

    try {
      const res = await fetch(API_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`, // 💥 SEND THE SECURE TOKEN TO DJANGO
        },
        body: JSON.stringify(payload),
      });

      // Check if Django rejected the token (401 Unauthorized)
      if (res.status === 401) {
        isAuthenticated.value = false;
        throw new Error("Session expired or unauthorized.");
      }

      const data = await res.json();
      isThinking.value = false;

      if (data.session_key && data.session_key !== "unknown")
        sessionKey.value = data.session_key;

      if (data.error)
        messages.value.push({ from: "vella", text: "⚠️ " + data.error });
      else if (data.action) {
        handleAction(data.action);
      } else if (data.revit_command) {
        if (
          data.revit_command.command &&
          data.revit_command.command.startsWith("wizard:")
        ) {
          handleAction(data.revit_command.command);
        } else {
          if (!data.revit_command.session_key)
            data.revit_command.session_key = sessionKey.value;
          data.revit_command.token = accessToken.value;

          const interactiveStartMessage =
            data.revit_command.command === "start_interactive_room_package"
              ? "Interactive Mode Started! Please click on the target room in your active Revit floor plan..."
              : null;

          const messageToShow = data.message || interactiveStartMessage;

          if (messageToShow) {
            messages.value.push({ from: "vella", text: messageToShow });
          }

          nextTick(() => {
            scrollToBottom();
            setTimeout(() => {
              sendToRevit({ revit_command: data.revit_command });
            }, 600);
          });
        }
      } else if (data.message)
        messages.value.push({ from: "vella", text: data.message });

      scrollToBottom();
    } catch (err) {
      isThinking.value = false;
      messages.value.push({
        from: "vella",
        text: "⚠️ Connection error or unauthorized.",
      });
      scrollToBottom();
    }
  }

  // -------------------------------------------------------------------
  // submitDirect — for forms that need a backend response without side
  // effects on the chat (no isThinking flag, no message push, no callback
  // routing). Returns the raw parsed JSON response.
  // -------------------------------------------------------------------
  async function submitDirect(payload: any): Promise<any> {
    const token = await getValidToken();
    if (!token) {
      return { status: "error", message: "Not authenticated." };
    }
    try {
      const res = await fetch(API_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(payload),
      });
      if (res.status === 401) {
        isAuthenticated.value = false;
        return { status: "error", message: "Session expired or unauthorized." };
      }
      return await res.json();
    } catch (err: any) {
      return { status: "error", message: err?.message || String(err) };
    }
  }

  return {
    messages,
    isThinking,
    chatContainer,
    scrollToBottom,
    clearChatAndMemory,
    handleUserSubmit,
    sendUserPrompt,
    sendToBackend,
    submitDirect,
  };
}
