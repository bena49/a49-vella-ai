// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/LinkedTagHelpers.cs
// ============================================================================
// Linked-model tag helpers — REVIT 2021 STUBBED VARIANT.
//
// The canonical implementation lives in revit-addin/ (Revit 2024/2025) and
// uses three APIs added in Revit 2022 / 2024:
//   - IndependentTag.GetTaggedReferences()  — Revit 2022+
//   - Reference.LinkedElementId             — Revit 2022+
//   - Reference.CreateLinkReference()       — Revit 2022+
//   - LinkedElementId.Value                 — Revit 2024+ (uses .IntegerValue here)
//
// None of those exist in Revit 2021's API. Rather than break compilation, we
// keep this file structurally identical to the 2024/2025 version so future
// ports drop in cleanly, but every public method short-circuits to an empty
// result. Net effect in Revit 2021: linked-model tagging is a no-op — the
// host pass in each tag strategy still works exactly as before, the linked
// pass enumerates zero links and exits.
//
// If you're chasing why "tag all doors in linked file" produces nothing in
// Revit 2021: this stub is the reason. The strategy isn't broken — Revit
// 2021's API simply can't build the link-aware Reference that
// IndependentTag.Create needs.
//
// USAGE PATTERN (per strategy) — same shape as 2024/2025
// ──────────────────────────────────────────────────────
//   foreach (var link in LinkedTagHelpers.EnumerateLinks(doc, view))   // ← yields nothing in 2021
//   {
//       var alreadyTagged = LinkedTagHelpers.CollectAlreadyTaggedLinkedIds(...); // ← empty in 2021
//       …
//       Reference linkedRef = LinkedTagHelpers.BuildLinkedReference(...);  // ← null in 2021
//       if (linkedRef == null) continue;
//       IndependentTag.Create(...);
//   }
// ============================================================================

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    /// <summary>
    /// One loaded RevitLinkInstance + cached link Document and link→host transform.
    /// Yielded by <see cref="LinkedTagHelpers.EnumerateLinks"/>. The shape matches
    /// the 2024/2025 version verbatim so strategy code is portable.
    /// </summary>
    public class LinkContext
    {
        public RevitLinkInstance Instance { get; set; }
        public Document Document { get; set; }
        /// <summary>
        /// Transform that converts coordinates from <see cref="Document"/>'s
        /// local coordinate system into the host doc's coordinates. Use
        /// <c>OfPoint</c> for points and <c>OfVector</c> for direction vectors.
        /// </summary>
        public Transform Transform { get; set; }
    }

    public static class LinkedTagHelpers
    {
        // ====================================================================
        // LINK ENUMERATION (stubbed — see file header)
        // ====================================================================
        /// <summary>
        /// Revit 2021: yields nothing. Linked-model tagging is unavailable
        /// because <see cref="Reference.CreateLinkReference"/> (used downstream
        /// to build the tag reference) does not exist in this Revit version.
        /// </summary>
        public static IEnumerable<LinkContext> EnumerateLinks(Document hostDoc, View view)
        {
            yield break;
        }

        // ====================================================================
        // ALREADY-TAGGED CHECK (stubbed — see file header)
        // ====================================================================
        /// <summary>
        /// Revit 2021: returns an empty set. <see cref="IndependentTag.GetTaggedReferences"/>
        /// and <see cref="Reference.LinkedElementId"/> were both added in Revit 2022, so
        /// there's no API path to inspect linked-element tag dedup in 2021.
        /// </summary>
        public static HashSet<long> CollectAlreadyTaggedLinkedIds(
            Document hostDoc, View view, ElementId linkInstanceId, BuiltInCategory targetCategory)
        {
            return new HashSet<long>();
        }

        // ====================================================================
        // LINKED REFERENCE BUILDER (stubbed — see file header)
        // ====================================================================
        /// <summary>
        /// Revit 2021: always returns null. <see cref="Reference.CreateLinkReference"/>
        /// was added in Revit 2022. Callers MUST check for null and skip linked
        /// tagging — every tag strategy already does this.
        /// </summary>
        public static Reference BuildLinkedReference(Element linkedElement, RevitLinkInstance link)
        {
            return null;
        }
    }
}
