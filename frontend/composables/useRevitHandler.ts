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

    // --- PROJECT INVENTORY (Rename wizard data) ---
    if (data.project_inventory) {
      updateInventoryProps(data.project_inventory);
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
