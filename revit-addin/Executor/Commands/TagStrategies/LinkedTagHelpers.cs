// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/LinkedTagHelpers.cs
// ============================================================================
// Shared helpers used by every tag strategy to extend host-element tagging
// to elements that live in *linked* Revit models.
//
// CONTEXT
// ───────
// FilteredElementCollector(doc, view.Id) only sees host-doc elements. To
// tag elements from a linked file we have to:
//   1. Enumerate RevitLinkInstance objects visible in the view.
//   2. For each loaded link, collect elements from the link's Document.
//   3. Transform every coordinate (location point, facing vector, bbox
//      corners) using `linkInstance.GetTotalTransform()` so visibility,
//      crop-region, and placement math run in *host* coordinates.
//   4. Build a link-aware Reference via
//      `new Reference(linkedElem).CreateLinkReference(linkInstance)` and
//      pass it to IndependentTag.Create — this is the key API call that
//      makes Revit treat the tag as a tag of the linked element.
//
// USAGE PATTERN (per strategy)
// ────────────────────────────
//   // 1. Existing host pass — UNCHANGED.
//
//   // 2. Linked pass — additive:
//   foreach (var link in LinkedTagHelpers.EnumerateLinks(doc, view))
//   {
//       var alreadyTagged = LinkedTagHelpers.CollectAlreadyTaggedLinkedIds(
//           doc, view, link.Instance.Id, TargetCategory);
//
//       var linkedElems = new FilteredElementCollector(link.Document)
//           .OfCategory(TargetCategory)
//           .OfClass(typeof(FamilyInstance));
//
//       foreach (FamilyInstance elem in linkedElems)
//       {
//           if (skipTagged && alreadyTagged.Contains(elem.Id.Value)) { ... }
//
//           // ALL coordinate math uses link.Transform.OfPoint / .OfVector
//           XYZ ptHost     = link.Transform.OfPoint((elem.Location as LocationPoint).Point);
//           XYZ facingHost = link.Transform.OfVector(elem.FacingOrientation);
//
//           // Visibility/placement: same helpers as host, just with host coords.
//           if (!TagHelpers.IsElementInCropRegion(view, ptHost)) continue;
//           XYZ tagPoint = …;
//
//           // Build link reference and tag.
//           Reference linkedRef = LinkedTagHelpers.BuildLinkedReference(elem, link.Instance);
//           if (linkedRef == null) continue;
//           IndependentTag.Create(doc, tagSymbol.Id, view.Id,
//               linkedRef, false, TagOrientation.Horizontal, tagPoint);
//       }
//   }
//
// SCOPE / LIMITATIONS (v1)
// ────────────────────────
//   - Workset / design-option visibility filtering on the linked side is
//     not applied. FilteredElementCollector(linkDoc, viewId) is illegal
//     because the host view doesn't belong to the link doc, so we collect
//     unfiltered and rely on crop-region + facing-wall + section-zone
//     checks to weed out off-screen elements.
//   - Phase resolution across docs is not attempted for room lookups;
//     callers fall back to FacingOrientation when GetRoomAtPoint returns
//     null in the link's doc.
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    /// <summary>
    /// One loaded RevitLinkInstance + cached link Document and link→host transform.
    /// Yielded by <see cref="LinkedTagHelpers.EnumerateLinks"/>.
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
        // LINK ENUMERATION
        // ====================================================================

        /// <summary>
        /// Yields every loaded RevitLinkInstance that's visible in the given
        /// view. Filters out unloaded links (<c>GetLinkDocument()</c> null)
        /// and links the user has hidden in this view.
        /// </summary>
        public static IEnumerable<LinkContext> EnumerateLinks(Document hostDoc, View view)
        {
            if (hostDoc == null || view == null) yield break;

            // Using (doc, view.Id) on the host collector respects the view's
            // graphic visibility — links hidden via VV / category overrides
            // are excluded. Unloaded links return null from GetLinkDocument
            // and are skipped below.
            FilteredElementCollector collector;
            try
            {
                collector = new FilteredElementCollector(hostDoc, view.Id)
                    .OfClass(typeof(RevitLinkInstance));
            }
            catch
            {
                yield break;
            }

            foreach (RevitLinkInstance link in collector)
            {
                Document linkDoc = null;
                Transform xform = null;
                try
                {
                    linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue;            // unloaded
                    xform = link.GetTotalTransform();
                    if (xform == null) continue;              // shouldn't happen, defensive
                }
                catch
                {
                    continue;
                }

                yield return new LinkContext
                {
                    Instance = link,
                    Document = linkDoc,
                    Transform = xform,
                };
            }
        }

        // ====================================================================
        // ALREADY-TAGGED CHECK (link-aware)
        // ====================================================================

        /// <summary>
        /// Returns the set of linked-element IDs (as long) that already have
        /// an IndependentTag in this view, scoped to the given link instance
        /// and the given target category.
        ///
        /// Mirrors the existing host-side <c>GetTaggedLocalElementIds</c>
        /// pre-collection that strategies do today, but for linked elements
        /// it has to inspect <c>GetTaggedReferences</c> and check each
        /// reference's <c>LinkedElementId</c> property.
        /// </summary>
        public static HashSet<long> CollectAlreadyTaggedLinkedIds(
            Document hostDoc, View view, ElementId linkInstanceId, BuiltInCategory targetCategory)
        {
            var ids = new HashSet<long>();
            if (hostDoc == null || view == null || linkInstanceId == null ||
                linkInstanceId == ElementId.InvalidElementId)
                return ids;

            FilteredElementCollector tagCollector;
            try
            {
                tagCollector = new FilteredElementCollector(hostDoc, view.Id)
                    .OfClass(typeof(IndependentTag));
            }
            catch
            {
                return ids;
            }

            foreach (IndependentTag tag in tagCollector)
            {
                IList<Reference> refs = null;
                try
                {
                    refs = tag.GetTaggedReferences();
                }
                catch { }
                if (refs == null) continue;

                foreach (Reference r in refs)
                {
                    if (r == null) continue;
                    try
                    {
                        // For linked references:
                        //   r.ElementId        = the LinkInstance element ID in the host doc
                        //   r.LinkedElementId  = the element ID inside the link doc
                        //
                        // We only care about references whose LinkInstance matches
                        // the one we're scoped to.
                        if (r.LinkedElementId == ElementId.InvalidElementId) continue;
                        if (r.ElementId != linkInstanceId) continue;

                        // Optionally verify category matches — skip if we can't
                        // resolve the linked element (broken reference).
                        ids.Add(r.LinkedElementId.Value);
                    }
                    catch { }
                }
            }

            // RoomTags (SpatialElementTag) are also stored separately for plan
            // views — handled by RoomTagStrategy directly because the API path
            // differs (RoomTag vs IndependentTag).

            return ids;
        }

        // ====================================================================
        // LINKED REFERENCE BUILDER
        // ====================================================================

        /// <summary>
        /// Builds a <see cref="Reference"/> for the linked element that
        /// <c>IndependentTag.Create</c> accepts. Returns null if the API
        /// rejects the element (very rare — usually an unsupported element type).
        /// </summary>
        public static Reference BuildLinkedReference(Element linkedElement, RevitLinkInstance link)
        {
            if (linkedElement == null || link == null) return null;
            try
            {
                Reference local = new Reference(linkedElement);
                if (local == null) return null;
                return local.CreateLinkReference(link);
            }
            catch
            {
                return null;
            }
        }
    }
}
