// composables/useWizards.ts
// Extracted from index.vue — Wizard states, props, action handlers

import { ref, nextTick, type Ref } from "vue";

interface ChatMessage {
  from: "user" | "vella";
  text: string;
}

export function useWizards(
  messages: Ref<ChatMessage[]>,
  scrollToBottom: () => Promise<void>,
  handleUserSubmit: (text: string) => void,
  sendToBackend: (payload: any) => void,
  sendToRevit: (data: any) => void,
  sessionKey: Ref<string | null>
) {
  // --- WIZARD VISIBILITY ---
  const showWizard = ref(false);
  const showSheetWizard = ref(false);
  const showCreatePlaceWizard = ref(false);
  const showRenameWizard = ref(false);
  const showRoomWizard = ref(false);
  const showAutoTagWizard = ref(false);
  const showHelp = ref(false);

  // --- WIZARD PROPS ---
  const wizardProps = ref<any>({});
  const sheetWizardProps = ref<any>({});
  const createPlaceWizardProps = ref<any>({});
  const renameWizardProps = ref<any>({});
  const roomWizardProps = ref<any>({});
  const autoTagWizardProps = ref<any>({});
  const wizardKey = ref(0);

  // --- ACTION DISPATCHER ---
  function handleAction(action: string) {
    if (action === "preflight_check") {
      messages.value.push({
        from: "vella",
        text: "⚡ Running Preflight Check...",
      });
      sendToBackend({
        message: "preflight check",
        session_key: sessionKey.value,
      });
      return;
    }
    if (action === "ui:help") showHelp.value = true;
    else if (action === "wizard:create_views") {
      wizardProps.value = { levels: [], templates: [], scopeBoxes: [] };
      showWizard.value = true;
      sendToRevit({
        command: "fetch_project_info",
        args: { types: ["levels", "templates", "scope_boxes"] },
      });
    } else if (action === "wizard:create_sheets") {
      sheetWizardProps.value = { levels: [], titleblocks: [] };
      showSheetWizard.value = true;
      sendToRevit({
        command: "fetch_project_info",
        args: { types: ["levels", "titleblocks"] },
      });
    } else if (action === "wizard:create_and_place") {
      createPlaceWizardProps.value = {
        levels: [],
        templates: [],
        scopeBoxes: [],
        titleblocks: [],
        existingSheets: [],
      };
      showCreatePlaceWizard.value = true;
      sendToRevit({
        command: "fetch_project_info",
        args: {
          types: [
            "levels",
            "templates",
            "scope_boxes",
            "titleblocks",
            "sheets",
          ],
        },
      });
    } else if (action === "wizard:rename_wizard") {
      renameWizardProps.value = { inventoryData: { sheets: [], views: [] } };
      sendToRevit({ command: "fetch_project_inventory" });
    } else if (action === "wizard:room_elevations") {
      roomWizardProps.value = {
        levels: [],
        templates: [],
        titleblocks: [],
        rooms: [],
      };
      showRoomWizard.value = true;
      // Ask Revit to fetch the data, including our new "rooms" collector!
      sendToRevit({
        command: "fetch_project_info",
        args: { types: ["levels", "templates", "titleblocks", "rooms"] },
      });
    } else if (action === "wizard:auto_tag_doors") {
      autoTagWizardProps.value = {
        doorTags: [],
        planViews: [],
      };
      showAutoTagWizard.value = true;
      // Ask Revit to fetch door tags and plan views
      sendToRevit({
        command: "fetch_project_info",
        args: { types: ["door_tags", "plan_views"] },
      });
    }
  }

  // --- WIZARD SUBMIT HANDLERS ---
  function handleHelpPrompt(promptText: string) {
    showHelp.value = false;
    handleUserSubmit(promptText);
  }

  function handleWizardSubmit(result: any) {
    showWizard.value = false;
    showSheetWizard.value = false;
    showCreatePlaceWizard.value = false;
    handleUserSubmit(result);
  }

  function handleBatchSubmit(updates: any[]) {
    showRenameWizard.value = false;
    sendToRevit({
      revit_command: {
        command: "execute_batch_update",
        raw: { updates: updates },
      },
    });
    messages.value.push({
      from: "vella",
      text: `🔄 Applying ${updates.length} updates...`,
    });
  }

  // 💥 DIRECT TO REVIT HANDLER
  function handleRoomElevationExecute(payload: any) {
    showRoomWizard.value = false;

    // 1. Put the interactive instruction in the chat UI FIRST
    messages.value.push({
      from: "vella",
      text: `Interactive Mode Started! Please click on the target room in your active Revit floor plan... (Stage: ${payload.stage || "CD"})`,
    });

    // 2. Wait for DOM, then give WebView2 600ms to paint before freezing Revit!
    nextTick(() => {
      scrollToBottom();
      setTimeout(() => {
        sendToRevit({
          revit_command: {
            command: payload.command,
            raw: payload,
          },
        });
      }, 600);
    });
  }

  // 💥 AUTO-TAG SUBMIT HANDLER
  function handleAutoTagSubmit(payload: any) {
    showAutoTagWizard.value = false;

    // Send directly to backend with auto_tag_doors intent
    messages.value.push({
      from: "vella",
      text: `🏷️ Tagging doors in ${payload.view_ids.length} view(s)...`,
    });

    sendToBackend({
      message: "auto_tag_doors",
      tag_family: payload.tag_family,
      tag_type: payload.tag_type,
      view_ids: payload.view_ids,
      skip_tagged: payload.skip_tagged,
      session_key: sessionKey.value,
    });
  }

  function closeWizard() {
    showWizard.value = false;
  }

  // --- UPDATE WIZARD PROPS FROM REVIT DATA ---
  function updateWizardProps(projectInfo: any) {
    wizardProps.value = {
      levels: projectInfo.levels || [],
      templates: projectInfo.templates || [],
      scopeBoxes: projectInfo.scope_boxes || [],
      initialStage: "CD",
    };
    sheetWizardProps.value = {
      levels: projectInfo.levels || [],
      titleblocks: projectInfo.titleblocks || [],
      initialStage: "CD",
    };
    createPlaceWizardProps.value = {
      levels: projectInfo.levels || [],
      templates: projectInfo.templates || [],
      scopeBoxes: projectInfo.scope_boxes || [],
      titleblocks: projectInfo.titleblocks || [],
      existingSheets: projectInfo.sheets || [],
      initialStage: "CD",
    };
    roomWizardProps.value = {
      levels: projectInfo.levels || [],
      templates: projectInfo.templates || [],
      titleblocks: projectInfo.titleblocks || [],
      rooms: projectInfo.rooms || [],
      initialStage: "CD",
    };
    // Auto-tag wizard gets door_tags and plan_views when available
    if (projectInfo.door_tags || projectInfo.plan_views) {
      autoTagWizardProps.value = {
        doorTags: projectInfo.door_tags || [],
        planViews: projectInfo.plan_views || [],
      };
    }
    wizardKey.value++;
  }

  function updateInventoryProps(projectInventory: any) {
    renameWizardProps.value = { inventoryData: projectInventory };
    showRenameWizard.value = true;
    wizardKey.value++;
  }

  return {
    // Visibility
    showWizard,
    showSheetWizard,
    showCreatePlaceWizard,
    showRenameWizard,
    showRoomWizard,
    showAutoTagWizard,
    showHelp,
    // Props
    wizardProps,
    sheetWizardProps,
    createPlaceWizardProps,
    renameWizardProps,
    roomWizardProps,
    autoTagWizardProps,
    wizardKey,
    // Handlers
    handleAction,
    handleHelpPrompt,
    handleWizardSubmit,
    handleBatchSubmit,
    handleRoomElevationExecute,
    handleAutoTagSubmit,
    closeWizard,
    // Props updaters (called from useRevitHandler)
    updateWizardProps,
    updateInventoryProps,
  };
}
