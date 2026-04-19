// composables/useRevitHandler.ts
// Extracted from index.vue — Revit message handler + preflight/repair result formatting

import { type Ref } from "vue";

interface ChatMessage {
  from: "user" | "vella";
  text: string;
}

// =====================================================================
// PREFLIGHT RESULT FORMATTER
// =====================================================================

function formatPreflightResult(r: any): string {
  if (r.status === "error") {
    return "❌ Preflight Error: " + r.message;
  }

  let msg = "⚡ PREFLIGHT REPORT\n━━━━━━━━━━━━━━━━━━\n\n";

  const vt = r.view_templates;
  const tb = r.titleblocks;
  const pp = r.project_parameters;

  const vtOk = vt.missing_count === 0 && vt.misconfigured_count === 0;
  const tbOk = tb.missing_count === 0;
  const ppOk = pp.missing_count === 0;

  msg += `View Templates: ${vt.present}/${vt.total_required} ${vtOk ? "✅" : "⚠️"}\n`;
  msg += `Titleblocks: ${tb.present}/${tb.total_required} ${tbOk ? "✅" : "⚠️"}\n`;
  msg += `Parameters: ${pp.present}/${pp.total_required} ${ppOk ? "✅" : "⚠️"}\n`;

  if (r.status === "all_clear") {
    msg += `\nRevit Version: ${r.revit_version} | Template: ${r.template_file}\n`;
    msg += `\n✅ All Clear! Your project meets A49 standards.\nYou can continue with Vella's assistance.`;
  } else {
    if (vt.missing_count > 0) {
      msg += `\n❌ Missing Templates (${vt.missing_count}):\n`;
      vt.missing.forEach((name: string) => {
        msg += `• ${name}\n`;
      });
    }
    if (vt.misnamed_count > 0) {
      msg += `\n🔄 Misnamed Templates (${vt.misnamed_count}):\n`;
      vt.misnamed.forEach((item: any) => {
        msg += `• "${item.current}" → "${item.expected}"\n`;
      });
    }
    if (vt.misconfigured_count > 0) {
      msg += `\n⚠️ Misconfigured Templates (${vt.misconfigured_count}):\n`;
      vt.misconfigured.forEach((item: any) => {
        msg += `• ${item.name}\n`;
        item.issues.forEach((issue: any) => {
          msg += `  └ ${issue.parameter}: expected "${issue.expected}", found "${issue.actual}"\n`;
        });
      });
    }
    if (tb.missing_count > 0) {
      msg += `\n❌ Missing Titleblocks (${tb.missing_count}):\n`;
      tb.missing.forEach((name: string) => {
        msg += `• ${name}\n`;
      });
    }
    if (tb.misnamed_count > 0) {
      msg += `\n🔄 Misnamed Titleblocks (${tb.misnamed_count}):\n`;
      tb.misnamed.forEach((item: any) => {
        msg += `• ${item.family}: "${item.current}" → "${item.expected}"\n`;
      });
    }
    if (pp.missing_count > 0) {
      msg += `\n❌ Missing Parameters (${pp.missing_count}):\n`;
      pp.missing.forEach((name: string) => {
        msg += `• ${name}\n`;
      });
    }
    msg += `\nRevit Version: ${r.revit_version} | Template: ${r.template_file}`;
    msg += `\n\n⚠️ Warning! Your project does not fully meet A49 standards.\nPlease say 'Yes' to let Vella fix the issues for you or say 'No' to fix this later.`;
  }

  return msg;
}

// =====================================================================
// REPAIR RESULT FORMATTER
// =====================================================================

function formatRepairResult(r: any): string {
  if (r.status === "error") {
    return "❌ Repair Error: " + r.message;
  }

  let msg = "🔧 REPAIR REPORT\n━━━━━━━━━━━━━━━━━━\n\n";

  if (r.summary) {
    if (r.summary.templates_transferred > 0)
      msg += `✅ ${r.summary.templates_transferred} missing template(s) transferred\n`;
    if (r.summary.parameters_fixed > 0)
      msg += `✅ ${r.summary.parameters_fixed} misconfigured template(s) fixed\n`;
    if (r.summary.templates_renamed > 0)
      msg += `✅ ${r.summary.templates_renamed} view template(s) renamed\n`;
    if (r.summary.titleblocks_renamed > 0)
      msg += `✅ ${r.summary.titleblocks_renamed} titleblock type(s) renamed\n`;
    if (r.summary.titleblocks_transferred > 0)
      msg += `✅ ${r.summary.titleblocks_transferred} missing titleblock(s) transferred\n`;

    if (r.summary.errors && r.summary.errors.length > 0) {
      msg += `\n⚠️ Errors (${r.summary.errors.length}):\n`;
      r.summary.errors.forEach((err: string) => {
        msg += `• ${err}\n`;
      });
    }
  }

  msg += `\n${r.message}`;

  if (r.status === "success") {
    msg += `\n\nRun Preflight Check again to verify all issues are resolved.`;
  }

  return msg;
}

// =====================================================================
// AUTO-TAG RESULT FORMATTER
// =====================================================================

function formatAutoTagResult(r: any): string {
  if (r.status === "error") {
    return "❌ Auto-Tag Error: " + r.message;
  }

  const rawCat = r.category || "element";
  const label = rawCat.charAt(0).toUpperCase() + rawCat.slice(1);
  const pluralLabel = label.endsWith('y') ? label.slice(0, -1) + 'ies' : label + 's';
  const lowerPlural = pluralLabel.toLowerCase();

  let msg = `🏷️ ${pluralLabel.toUpperCase()} TAG REPORT\n━━━━━━━━━━━━━━━━━━\n\n`;

  msg += `Tag Family: ${r.tag_family || "—"} : ${r.tag_type || "—"}\n`;
  msg += `Views Processed: ${r.views_processed || 0}\n`;

  // Dynamic Icon for Count: ❌ if zero, ✅ if some were tagged
  const statusIcon = r.tagged_count > 0 ? "✅" : "❌";
  msg += `${pluralLabel} Tagged: ${r.tagged_count || 0} ${statusIcon}\n`;

  if (r.skipped_count > 0) {
    msg += `Already Tagged (Skipped): ${r.skipped_count} ⏭️\n`;
  }

  if (r.errors && r.errors.length > 0) {
    msg += `\n⚠️ Warnings (${r.errors.length}):\n`;
    r.errors.forEach((err: string) => { msg += `• ${err}\n`; });
  }

  // =====================================================================
  // SMART FOOTER LOGIC (Scenarios 1-5)
  // =====================================================================
  const tagged = r.tagged_count || 0;
  const skipped = r.skipped_count || 0;
  const total = tagged + skipped;

  msg += "\n"; // Padding before footer

  if (total === 0) {
    // Scenario 3: No elements found at all
    msg += `⚠️ Please check your view/s for available '${lowerPlural}' to tag!`;
  } 
  else if (tagged > 0 && skipped === 0) {
    // Scenario 1: Fresh tagging, everything successful
    msg += `✅ All ${lowerPlural} tagged successfully!`;
  } 
  else if (tagged > 0 && skipped > 0) {
    // Scenario 2 & 5: Mixed results across one or many views
    msg += `✅ Remaining untagged ${lowerPlural} tagged successfully!`;
  } 
  else if (tagged === 0 && skipped > 0) {
    // Scenario 4: Nothing to do, all already tagged
    msg += `⚠️ All ${lowerPlural} have already been tagged!`;
  }

  return msg;
}

// =====================================================================
// AUTO-DIM RESULT FORMATTER
// =====================================================================

function formatAutoDimResult(r: any): string {
  if (r.status === "error") {
    return "❌ Auto-Dim Error: " + r.message;
  }

  const dimensioned    = r.dimensioned    || 0;
  const skipped        = r.skipped        || 0;
  const failed         = r.failed         || 0;
  const viewsProcessed = r.views_processed || 0;
  const totalWalls     = r.total_walls    || 0;

  let msg = `📐 DIMENSION REPORT\n━━━━━━━━━━━━━━━━━━\n\n`;
  msg += `Views Processed: ${viewsProcessed}\n`;
  msg += `Walls Found: ${totalWalls}\n`;

  const statusIcon = dimensioned > 0 ? "✅" : "❌";
  msg += `Walls Dimensioned: ${dimensioned} ${statusIcon}\n`;

  if (skipped > 0) msg += `Skipped: ${skipped} ⏭️\n`;
  if (failed  > 0) msg += `Failed: ${failed} ⚠️\n`;

  if (r.errors && r.errors.length > 0) {
    msg += `\n⚠️ Warnings (${r.errors.length}):\n`;
    r.errors.slice(0, 5).forEach((err: string) => { msg += `• ${err}\n`; });
    if (r.errors.length > 5) msg += `  ...and ${r.errors.length - 5} more\n`;
  }

  msg += "\n";
  if (totalWalls === 0) {
    msg += `⚠️ No walls found in the selected view(s). Check your view filters.`;
  } else if (dimensioned > 0 && failed === 0) {
    msg += `✅ Dimensioning complete!`;
  } else if (dimensioned > 0 && failed > 0) {
    msg += `⚠️ Partially complete — some walls could not be dimensioned.`;
  } else {
    msg += `❌ No walls were dimensioned. Check the warnings above.`;
  }

  return msg;
}

// =====================================================================
// MAIN HANDLER FACTORY
// =====================================================================

export function useRevitHandler(
  messages: Ref<ChatMessage[]>,
  sessionKey: Ref<string | null>,
  scrollToBottom: () => Promise<void>,
  sendUserPrompt: (revitData?: any) => Promise<void>,
  updateWizardProps: (projectInfo: any) => void,
  updateInventoryProps: (projectInventory: any) => void
) {
  async function handleRevitMessage(raw: any) {
    let data = raw;
    if (typeof raw === "string") {
      try {
        data = JSON.parse(raw);
      } catch (e) {
        return;
      }
    }

    if (data.session_key) sessionKey.value = data.session_key;

    // --- PROJECT INFO (Wizard data from Revit) ---
    if (data.project_info) {
      updateWizardProps(data.project_info);
      return;
    }

    // --- PROJECT INVENTORY (Rename wizard data)
    // Guard: only process if the key is present AND non-empty.
    // Stale queued messages from previous sessions can otherwise
    // trigger the Rename Wizard unexpectedly behind other wizards.
    if (data.project_inventory && Object.keys(data.project_inventory).length > 0) {
      updateInventoryProps(data.project_inventory);
      return;
    }

    // --- ROOM PACKAGE COMPLETION SUMMARY ---
    // Extract nested result if the C# wrapper is still present
    const payload = data.result && typeof data.result === 'string' && data.result.includes("ROOM_PACKAGE_COMPLETE") 
                    ? JSON.parse(data.result) 
                    : data;

    if (payload.type === "ROOM_PACKAGE_COMPLETE") {
      const summaryText = `✅ **Room Package Complete!**\n\n` +
                          `**Room:** ${payload.room_name}\n` +
                          `**Created Sheet:** ${payload.sheet_number} - ${payload.sheet_name}\n` +
                          `**Views Generated:** ${payload.view_count}\n\n` +
                          `The new sheet has been opened in Revit for your review.`;

      messages.value.push({ from: "vella", text: summaryText });
      scrollToBottom();
      return;
    }

    // --- PREFLIGHT RESULT ---
    if (data.preflight_result) {
      const r = data.preflight_result;
      const msg = formatPreflightResult(r);

      if (r.status !== "error") {
        (window as any).__vellaPreflightResult = r;
      }

      messages.value.push({ from: "vella", text: msg });
      scrollToBottom();
      return;
    }

    // --- PREFLIGHT REPAIR RESULT ---
    if (data.preflight_repair_result) {
      const msg = formatRepairResult(data.preflight_repair_result);
      messages.value.push({ from: "vella", text: msg });
      scrollToBottom();
      return;
    }

    // --- AUTO-TAG RESULT ---
    if (data.auto_tag_result) {
      const msg = formatAutoTagResult(data.auto_tag_result);
      messages.value.push({ from: "vella", text: msg });
      scrollToBottom();
      return;
    }

    // --- AUTO-DIM RESULT ---
    if (data.auto_dim_result) {
      const msg = formatAutoDimResult(data.auto_dim_result);
      messages.value.push({ from: "vella", text: msg });
      scrollToBottom();
      return;
    }

    // --- SILENT / SUCCESS SUPPRESSION ---
    if (data.status === "silent") return;
    if (data.result === "✔ Command executed successfully.") return;

    // --- LIST RESULTS (Forward to backend) ---
    if (
      data.list_views_result ||
      data.list_sheets_result ||
      data.list_views_on_sheet_result ||
      data.list_scope_boxes_result
    ) {
      if (!data.session_key && sessionKey.value)
        data.session_key = sessionKey.value;
      await sendUserPrompt(data);
      return;
    }

    // --- GENERIC MESSAGE ---
    const text = data.error
      ? "⚠️ " + data.error
      : data.message || data.result;
    if (text && typeof text === "string") {
      if (text.trim().startsWith("{")) return;
      messages.value.push({ from: "vella", text });
      scrollToBottom();
    }
  }

  return {
    handleRevitMessage,
  };
}
