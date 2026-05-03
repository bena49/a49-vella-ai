// composables/useWizards.ts
// Extracted from index.vue — Wizard states, props, action handlers and Revit data updaters.

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
  const showWizard            = ref(false);
  const showSheetWizard       = ref(false);
  const showCreatePlaceWizard = ref(false);
  const showRenameWizard      = ref(false);
  const showRoomWizard        = ref(false);
  const showAutomateTagWizard = ref(false);
  const showAutomateDimWizard = ref(false);
  const showInsertStandardDetailsWizard = ref(false);
  const showHelp              = ref(false);

  // --- WIZARD PROPS ---
  const wizardProps            = ref<any>({});
  const sheetWizardProps       = ref<any>({});
  const createPlaceWizardProps = ref<any>({});
  const renameWizardProps      = ref<any>({});
  const roomWizardProps        = ref<any>({});
  const automateTagWizardProps = ref<any>({});
  const automateDimWizardProps = ref<any>({});
  const insertStandardDetailsWizardProps = ref<any>({ previewResult: null });
  const wizardKey              = ref(0);

  // --- ACTION DISPATCHER ---
  function handleAction(action: string) {
    if (action === "preflight_check") {
      messages.value.push({ from: "vella", text: "⚡ Running Preflight Check..." });
      sendToBackend({ message: "preflight check", session_key: sessionKey.value });
      return;
    }

    if (action === "ui:help") {
      showHelp.value = true;
    }
    else if (action === "wizard:create_views") {
      wizardProps.value = { levels: [], templates: [], scopeBoxes: [] };
      showWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "templates", "scope_boxes"] } });
    }
    else if (action === "wizard:create_sheets") {
      sheetWizardProps.value = { levels: [], titleblocks: [] };
      showSheetWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "titleblocks"] } });
    }
    else if (action === "wizard:create_and_place") {
      createPlaceWizardProps.value = { levels: [], templates: [], scopeBoxes: [], titleblocks: [], existingSheets: [] };
      showCreatePlaceWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "templates", "scope_boxes", "titleblocks", "sheets"] } });
    }
    else if (action === "wizard:rename_wizard") {
      renameWizardProps.value = { inventoryData: { sheets: [], views: [] } };
      sendToRevit({ command: "fetch_project_inventory" });
    }
    else if (action === "wizard:room_elevations") {
      roomWizardProps.value = { levels: [], templates: [], titleblocks: [], rooms: [] };
      showRoomWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "templates", "titleblocks", "rooms"] } });
    }
    else if (action === "wizard:automate_tag") {
      automateTagWizardProps.value = { doorTags: [], windowTags: [], wallTags: [], roomTags: [], ceilingTags: [], spotElevationTags: [], taggableViews: [] };
      showAutomateTagWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["door_tags", "window_tags", "wall_tags", "room_tags", "ceiling_tags", "spot_elevation_tags", "taggable_views"] } });
    }
    else if (action === "wizard:automate_dim") {
      automateDimWizardProps.value = { dimTypes: [], floorPlanViews: [] };
      showAutomateDimWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["dim_types", "floor_plan_views"] } });
    }
    else if (action === "wizard:insert_standard_details") {
      insertStandardDetailsWizardProps.value = { previewResult: null };
      showInsertStandardDetailsWizard.value = true;
      // No initial fetch — preview is requested per-card-pick from inside the wizard.
    }
  }

  // --- SUBMIT HANDLERS ---
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
    sendToRevit({ revit_command: { command: "execute_batch_update", raw: { updates } } });
    messages.value.push({ from: "vella", text: `🔄 Applying ${updates.length} updates...` });
  }

  function handleRoomElevationExecute(payload: any) {
    showRoomWizard.value = false;
    messages.value.push({
      from: "vella",
      text: `Interactive Mode Started! Please click on the target room in your active Revit floor plan... (Stage: ${payload.stage || "CD"})`,
    });
    nextTick(() => {
      scrollToBottom();
      setTimeout(() => {
        sendToRevit({ revit_command: { command: payload.command, raw: payload } });
      }, 600);
    });
  }

  function handleAutomateTagSubmit(payload: any) {
    showAutomateTagWizard.value = false;
    const elementNames: Record<string, string> = {
      door: "door", window: "window", wall: "wall", room: "room", ceiling: "ceiling",
      spot_elevation: "spot elevation",
    };
    const elementLabel = elementNames[payload.tag_category] || "element";
    messages.value.push({ from: "vella", text: `🏷️ Tagging ${elementLabel}s in ${payload.view_ids.length} view(s)...` });
    sendToBackend({
      message:            "automate_tag",
      tag_category:       payload.tag_category,
      tag_family:         payload.tag_family,
      tag_type:           payload.tag_type,
      view_ids:           payload.view_ids,
      skip_tagged:        payload.skip_tagged,
      spot_plan_type:     payload.spot_plan_type    ?? "",
      spot_section_type:  payload.spot_section_type ?? "",
      session_key:        sessionKey.value,
    });
  }

  // 💥 AUTOMATE DIM SUBMIT HANDLER
  function handleAutomateDimSubmit(payload: any) {
    showAutomateDimWizard.value = false;
    messages.value.push({
      from: "vella",
      text: `📐 Dimensioning walls in ${payload.view_ids.length} view(s)...`,
    });
    sendToBackend({
      message:            "automate_dim",
      view_ids:           payload.view_ids,
      include_openings:   payload.include_openings,
      include_grids:      payload.include_grids,
      include_total:      payload.include_total,
      include_grids_only: payload.include_grids_only,
      include_detail:     payload.include_detail ?? true,
      include_interior:   payload.include_interior ?? true,
      offset_mm:          payload.offset_mm ?? 1600,
      inset_mm:           payload.inset_mm ?? 1200,
      depth_mm:           payload.depth_mm ?? 5000,
      smart_exterior:     payload.smart_exterior,
      dim_type_name:      payload.dim_type_name,
      session_key:        sessionKey.value,
    });
  }

  // 💥 INSERT STANDARD DETAILS — request preview (when card picked)
  function handleInsertStandardDetailsRequestPreview(payload: any) {
    sendToBackend({
      message:     "insert_standard_details",
      mode:        "preview",
      package:     payload.package,
      session_key: sessionKey.value,
    });
  }

  // 💥 INSERT STANDARD DETAILS — submit (Browse clicked)
  function handleInsertStandardDetailsSubmit(payload: any) {
    showInsertStandardDetailsWizard.value = false;
    const label = payload.package === "standard" ? "Standard" : "EIA";
    messages.value.push({
      from: "vella",
      text: `📚 Opening Insert Views for ${label} Details — paste path with Ctrl+V in the Revit dialog.`,
    });
    sendToBackend({
      message:     "insert_standard_details",
      mode:        "execute",
      package:     payload.package,
      session_key: sessionKey.value,
    });
  }

  // 💥 INSERT STANDARD DETAILS — apply preview/execute result from Revit
  function applyInsertStandardDetailsResult(result: any) {
    if (!result) return;
    if (result.mode === "preview") {
      // Push to wizard via reactive prop. Replace the whole props object so Vue
      // sees the change even when keys are deeply equal.
      insertStandardDetailsWizardProps.value = { previewResult: result };
      return;
    }
    // mode === "execute" → result message goes into chat (handled in useRevitHandler)
  }

  function closeWizard() {
    showWizard.value = false;
  }

  // --- UPDATE WIZARD PROPS FROM REVIT DATA ---
  function updateWizardProps(projectInfo: any) {
    wizardProps.value = {
      levels: projectInfo.levels || [], templates: projectInfo.templates || [],
      scopeBoxes: projectInfo.scope_boxes || [], initialStage: "CD",
    };
    sheetWizardProps.value = {
      levels: projectInfo.levels || [], titleblocks: projectInfo.titleblocks || [], initialStage: "CD",
    };
    createPlaceWizardProps.value = {
      levels: projectInfo.levels || [], templates: projectInfo.templates || [],
      scopeBoxes: projectInfo.scope_boxes || [], titleblocks: projectInfo.titleblocks || [],
      existingSheets: projectInfo.sheets || [], initialStage: "CD",
    };
    roomWizardProps.value = {
      levels: projectInfo.levels || [], templates: projectInfo.templates || [],
      titleblocks: projectInfo.titleblocks || [], rooms: projectInfo.rooms || [], initialStage: "CD",
    };

    // Automate tag wizard
    automateTagWizardProps.value = {
      doorTags:          projectInfo.door_tags           || [],
      windowTags:        projectInfo.window_tags         || [],
      wallTags:          projectInfo.wall_tags           || [],
      roomTags:          projectInfo.room_tags           || [],
      ceilingTags:       projectInfo.ceiling_tags        || [],
      spotElevationTags: projectInfo.spot_elevation_tags || [],
      taggableViews:     projectInfo.taggable_views      || [],
    };

    // Automate dim wizard
    automateDimWizardProps.value = {
      dimTypes:       projectInfo.dim_types       || [],
      floorPlanViews: projectInfo.floor_plan_views || [],
    };

    // Cache tag inventory to backend for NLP tagging
    if (projectInfo.taggable_views && projectInfo.taggable_views.length > 0) {
      sendToBackend({
        message:       "cache_tag_inventory",
        taggable_views: projectInfo.taggable_views || [],
        door_tags:     projectInfo.door_tags     || [],
        window_tags:   projectInfo.window_tags   || [],
        wall_tags:     projectInfo.wall_tags     || [],
        room_tags:     projectInfo.room_tags     || [],
        ceiling_tags:          projectInfo.ceiling_tags        || [],
        spot_elevation_tags:   projectInfo.spot_elevation_tags || [],
        session_key:           sessionKey.value,
      });
    }

    // Cache floor plan views to backend for NLP dimensioning
    if (projectInfo.floor_plan_views && projectInfo.floor_plan_views.length > 0) {
      sendToBackend({
        message:          "cache_dim_inventory",
        floor_plan_views: projectInfo.floor_plan_views,
        session_key:      sessionKey.value,
      });
    }

    // Cache the project's actual level names to backend so the level_matcher
    // can resolve user input ("L2", "ชั้น 2", "+5.50 ระดับพื้นชั้น 2") to the
    // project's exact Revit level name regardless of naming convention.
    if (projectInfo.levels && projectInfo.levels.length > 0) {
      sendToBackend({
        message:     "cache_level_inventory",
        levels:      projectInfo.levels,
        session_key: sessionKey.value,
      });
    }

    wizardKey.value++;
  }

  function updateInventoryProps(projectInventory: any) {
    if (!projectInventory || Object.keys(projectInventory).length === 0) return;
    renameWizardProps.value = { inventoryData: projectInventory };
    showRenameWizard.value = true;
    wizardKey.value++;
  }

  return {
    // Visibility
    showWizard, showSheetWizard, showCreatePlaceWizard,
    showRenameWizard, showRoomWizard, showAutomateTagWizard,
    showAutomateDimWizard, showInsertStandardDetailsWizard, showHelp,
    // Props
    wizardProps, sheetWizardProps, createPlaceWizardProps,
    renameWizardProps, roomWizardProps, automateTagWizardProps,
    automateDimWizardProps, insertStandardDetailsWizardProps, wizardKey,
    // Handlers
    handleAction, handleHelpPrompt, handleWizardSubmit, handleBatchSubmit,
    handleRoomElevationExecute, handleAutomateTagSubmit, handleAutomateDimSubmit,
    handleInsertStandardDetailsRequestPreview, handleInsertStandardDetailsSubmit,
    applyInsertStandardDetailsResult,
    closeWizard,
    // Props updaters
    updateWizardProps, updateInventoryProps,
  };
}
